using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Infrastructure.Sqlite;

namespace TkpSalaryCalculator.Infrastructure.DataTransfer;

/// <summary>データレコードを単一 JSON 文書へ逐次書き込みます。</summary>
public sealed class StreamingJsonExportStream : IJsonExportStream
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task WriteAsync(Stream destination, ExportDocumentHeader header,
        IAsyncEnumerable<DataTransferRecord> records, CancellationToken cancellationToken) =>
        BackgroundOperation.RunAsync(
            () => WriteCoreAsync(destination, header, records, cancellationToken), cancellationToken);

    private static async Task WriteCoreAsync(Stream destination, ExportDocumentHeader header,
        IAsyncEnumerable<DataTransferRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(records);
        if (!destination.CanWrite) throw new ArgumentException("Destination must be writable.", nameof(destination));
        JsonTransferLimits.ValidateString(header.Format, "format");
        JsonTransferLimits.ValidateString(header.AppVersion, "appVersion");

        await using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = false });
        writer.WriteStartObject();
        writer.WriteString("format", header.Format);
        writer.WriteNumber("formatVersion", header.FormatVersion);
        writer.WriteString("createdAtUtc", header.CreatedAtUtc.ToUniversalTime());
        writer.WriteString("appVersion", header.AppVersion);
        writer.WriteStartArray("data");
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            ArgumentNullException.ThrowIfNull(record);
            writer.WriteStartObject();
            writer.WriteString("section", record.Section.ToString());
            writer.WriteNumber("sequence", record.Sequence);
            writer.WritePropertyName("value");
            var value = GetRecordValue(record);
            var element = value is JsonElement jsonElement
                ? jsonElement
                : JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object), SerializerOptions);
            JsonTransferLimits.ValidateElement(element);
            element.WriteTo(writer);
            writer.WriteEndObject();
            // DATA-010: never retain the sequence and push each completed record downstream.
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static object? GetRecordValue(DataTransferRecord record)
    {
        var property = record.GetType().GetProperty("Value");
        return property is null
            ? throw new InvalidDataException($"Unsupported transfer record type '{record.GetType().FullName}'.")
            : property.GetValue(record);
    }
}

/// <summary>単一 JSON 文書を固定サイズバッファで解析し、レコードを逐次返します。</summary>
public sealed class StreamingJsonImportStream : IJsonImportStream
{
    private const int BufferSize = 64 * 1024;

    public IAsyncEnumerable<DataTransferRecord> ReadAsync(Stream source,
        CancellationToken cancellationToken) =>
        BackgroundOperation.StreamAsync(token => ReadCoreAsync(source, token), cancellationToken);

    private static async IAsyncEnumerable<DataTransferRecord> ReadCoreAsync(Stream source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("Source must be readable.", nameof(source));

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize * 2);
        var buffered = 0;
        var state = new JsonReaderState(new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64,
        });
        string? currentProperty = null;
        string? format = null;
        string? appVersion = null;
        string? createdAt = null;
        int? formatVersion = null;
        var inData = false;
        var headerEmitted = false;
        var rootCompleted = false;
        MemoryStream? recordBuffer = null;
        Utf8JsonWriter? recordWriter = null;
        var captureDepth = 0;
        var firstBuffer = true;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (buffered >= JsonTransferLimits.MaxTokenBytes)
                    throw new InvalidDataException("A JSON token exceeds the supported streaming buffer size.");
                if (buffered == buffer.Length)
                {
                    var newSize = Math.Min(checked(buffer.Length * 2), JsonTransferLimits.MaxTokenBytes);
                    var replacement = ArrayPool<byte>.Shared.Rent(newSize);
                    Buffer.BlockCopy(buffer, 0, replacement, 0, buffered);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = replacement;
                }
                var writable = Math.Min(buffer.Length, JsonTransferLimits.MaxTokenBytes) - buffered;
                var read = await source.ReadAsync(buffer.AsMemory(buffered, writable), cancellationToken)
                    .ConfigureAwait(false);
                buffered += read;
                var isFinal = read == 0;
                if (firstBuffer)
                {
                    firstBuffer = false;
                    if (buffered >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                    {
                        Buffer.BlockCopy(buffer, 3, buffer, 0, buffered - 3);
                        buffered -= 3;
                    }
                }

                var ready = new List<DataTransferRecord>();
                int consumed;
                {
                    var reader = new Utf8JsonReader(buffer.AsSpan(0, buffered), isFinal, state);
                    while (reader.Read())
                    {
                        if (recordWriter is not null)
                        {
                            CopyToken(ref reader, recordWriter);
                            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) captureDepth++;
                            else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) captureDepth--;
                            if (captureDepth == 0)
                            {
                                recordWriter.Flush();
                                recordWriter.Dispose();
                                recordWriter = null;
                                recordBuffer!.Position = 0;
                                ready.Add(ParseEnvelope(recordBuffer));
                                recordBuffer.Dispose();
                                recordBuffer = null;
                            }

                            continue;
                        }

                        if (inData)
                        {
                            if (reader.TokenType == JsonTokenType.EndArray)
                            {
                                inData = false;
                                continue;
                            }

                            if (reader.TokenType != JsonTokenType.StartObject)
                                throw new InvalidDataException("Every data item must be a JSON object.");
                            recordBuffer = new MemoryStream();
                            recordWriter = new Utf8JsonWriter(recordBuffer);
                            captureDepth = 1;
                            CopyToken(ref reader, recordWriter);
                            continue;
                        }

                        switch (reader.TokenType)
                        {
                            case JsonTokenType.PropertyName:
                                currentProperty = reader.GetString();
                                JsonTransferLimits.ValidateString(currentProperty!, "property name");
                                break;
                            case JsonTokenType.String when currentProperty == "format":
                                format = reader.GetString();
                                JsonTransferLimits.ValidateString(format!, "format");
                                currentProperty = null;
                                break;
                            case JsonTokenType.Number when currentProperty == "formatVersion":
                                formatVersion = reader.GetInt32();
                                currentProperty = null;
                                break;
                            case JsonTokenType.String when currentProperty == "createdAtUtc":
                                createdAt = reader.GetString();
                                JsonTransferLimits.ValidateString(createdAt!, "createdAtUtc");
                                currentProperty = null;
                                break;
                            case JsonTokenType.String when currentProperty == "appVersion":
                                appVersion = reader.GetString();
                                JsonTransferLimits.ValidateString(appVersion!, "appVersion");
                                currentProperty = null;
                                break;
                            case JsonTokenType.StartArray when currentProperty == "data":
                                if (format is null || formatVersion is null || createdAt is null || appVersion is null)
                                    throw new InvalidDataException("The export header is incomplete or precedes required fields.");
                                var header = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
                                {
                                    ["type"] = "document_header",
                                    ["format"] = format,
                                    ["formatVersion"] = formatVersion.Value,
                                    ["createdAtUtc"] = createdAt,
                                    ["appVersion"] = appVersion,
                                });
                                ready.Add(new DataTransferRecord<JsonElement>(DataTransferSection.Metadata, 0, header));
                                headerEmitted = true;
                                inData = true;
                                currentProperty = null;
                                break;
                            case JsonTokenType.EndObject when reader.CurrentDepth == 0:
                                rootCompleted = true;
                                break;
                        }
                    }

                    consumed = checked((int)reader.BytesConsumed);
                    state = reader.CurrentState;
                }

                if (consumed != 0)
                {
                    Buffer.BlockCopy(buffer, consumed, buffer, 0, buffered - consumed);
                    buffered -= consumed;
                }

                foreach (var record in ready) yield return record;

                if (!isFinal) continue;
                if (buffered != 0 || recordWriter is not null || inData || !headerEmitted || !rootCompleted)
                    throw new InvalidDataException("The JSON document ended before it was complete.");
                break;
            }
        }
        finally
        {
            recordWriter?.Dispose();
            recordBuffer?.Dispose();
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static DataTransferRecord ParseEnvelope(Stream stream)
    {
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        if (!root.TryGetProperty("section", out var sectionElement) ||
            !Enum.TryParse<DataTransferSection>(sectionElement.GetString(), false, out var section) ||
            !root.TryGetProperty("sequence", out var sequenceElement) ||
            !sequenceElement.TryGetInt64(out var sequence) || sequence < 0 ||
            !root.TryGetProperty("value", out var value))
        {
            throw new InvalidDataException("A transfer record envelope is invalid.");
        }

        return new DataTransferRecord<JsonElement>(section, sequence, value.Clone());
    }

    private static void CopyToken(ref Utf8JsonReader reader, Utf8JsonWriter writer)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                writer.WriteStartObject();
                break;
            case JsonTokenType.EndObject:
                writer.WriteEndObject();
                break;
            case JsonTokenType.StartArray:
                writer.WriteStartArray();
                break;
            case JsonTokenType.EndArray:
                writer.WriteEndArray();
                break;
            case JsonTokenType.PropertyName:
                {
                    var value = reader.GetString()!;
                    JsonTransferLimits.ValidateString(value, "property name");
                    writer.WritePropertyName(value);
                    break;
                }
            case JsonTokenType.String:
                {
                    var value = reader.GetString();
                    if (value is not null) JsonTransferLimits.ValidateString(value, "string value");
                    writer.WriteStringValue(value);
                    break;
                }
            case JsonTokenType.Number:
                if (reader.HasValueSequence)
                    writer.WriteRawValue(reader.ValueSequence.ToArray(), skipInputValidation: true);
                else
                    writer.WriteRawValue(reader.ValueSpan, skipInputValidation: true);
                break;
            case JsonTokenType.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonTokenType.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonTokenType.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException($"Unsupported JSON token {reader.TokenType}.");
        }
    }
}

internal static class JsonTransferLimits
{
    // A successful export is always readable by this implementation. Four MiB of decoded UTF-8
    // allows substantially larger values than the previous 128 KiB token buffer, while the 32 MiB
    // encoded-token cap accounts for worst-case JSON escaping and bounds parser memory use.
    internal const int MaxStringUtf8Bytes = 4 * 1024 * 1024;
    internal const int MaxTokenBytes = 32 * 1024 * 1024;

    internal static void ValidateString(string value, string field)
    {
        if (Encoding.UTF8.GetByteCount(value) > MaxStringUtf8Bytes)
            throw new InvalidDataException(
                $"JSON {field} exceeds the supported {MaxStringUtf8Bytes}-byte UTF-8 limit.");
    }

    internal static void ValidateElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ValidateString(property.Name, "property name");
                    ValidateElement(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) ValidateElement(item);
                break;
            case JsonValueKind.String:
                ValidateString(element.GetString()!, "string value");
                break;
        }
    }
}
