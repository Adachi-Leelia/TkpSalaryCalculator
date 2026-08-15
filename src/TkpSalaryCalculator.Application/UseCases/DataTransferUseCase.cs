using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Internal;
using TkpSalaryCalculator.Application.Ports;

namespace TkpSalaryCalculator.Application.UseCases;

/// <summary>ストリーミングエクスポートと、確認前に隔離する全置換インポートを実装します。</summary>
/// <remarks>必要なストリーム、ステージング、およびメタデータポートを指定して生成します。</remarks>
public sealed class DataTransferUseCase(IJsonExportStream exportStream, IJsonImportStream importStream,
    IExportDataSource exportData, IImportStagingRepository staging, IAppMetadataRepository metadata,
    ITransactionRunner transactions, IUtcClock clock) : IDataTransferUseCase
{
    /// <summary>現在の安定したエクスポート形式識別子です。</summary>
    public const string FormatName = "tkp-salary-calculator";
    /// <summary>現在対応するエクスポート形式バージョンです。</summary>
    public const int CurrentFormatVersion = 1;
    private const int BatchSize = 256;

    private readonly IJsonExportStream exportStream = exportStream ?? throw new ArgumentNullException(nameof(exportStream));
    private readonly IJsonImportStream importStream = importStream ?? throw new ArgumentNullException(nameof(importStream));
    private readonly IExportDataSource exportData = exportData ?? throw new ArgumentNullException(nameof(exportData));
    private readonly IImportStagingRepository staging = staging ?? throw new ArgumentNullException(nameof(staging));
    private readonly IAppMetadataRepository metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    private readonly ITransactionRunner transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));


    /// <inheritdoc />
    public Task<DataTransferFormatDto> GetFormatAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DataTransferFormatDto(FormatName, CurrentFormatVersion));
    }

    /// <inheritdoc />
    public async Task ExportAsync(Stream destination, string appVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("書き込み可能な出力先を指定してください。", nameof(destination));
        if (string.IsNullOrWhiteSpace(appVersion)) throw new ArgumentException("アプリバージョンを指定してください。", nameof(appVersion));
        cancellationToken.ThrowIfCancellationRequested();
        var now = clock.UtcNow.ToUniversalTime();
        await using (var session = await exportData.OpenReadSessionAsync(cancellationToken).ConfigureAwait(false))
        {
            await exportStream.WriteAsync(destination,
                new ExportDocumentHeader(FormatName, CurrentFormatVersion, now, appVersion.Trim()),
                session.StreamAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        await transactions.ExecuteAsync(token => metadata.SetLastExportedAtUtcAsync(now, token), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportPreviewDto> PrepareImportAsync(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("読み取り可能な入力元を指定してください。", nameof(source));
        cancellationToken.ThrowIfCancellationRequested();
        await staging.DiscardAbandonedAsync(cancellationToken).ConfigureAwait(false);
        var id = await staging.CreateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var batch = new List<DataTransferRecord>(BatchSize);
            await foreach (var record in importStream.ReadAsync(source, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (record is null) throw new ApplicationErrorException("IMPORT_RECORD_INVALID", "インポートファイルに不正なデータがあります。");
                batch.Add(record);
                if (batch.Count < BatchSize) continue;
                await staging.AppendBatchAsync(id, [.. batch], cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }
            if (batch.Count != 0) await staging.AppendBatchAsync(id, batch, cancellationToken).ConfigureAwait(false);
            var preview = await staging.ValidateAsync(id, cancellationToken).ConfigureAwait(false);
            if (preview.Id != id) throw new ApplicationErrorException("IMPORT_TOKEN_MISMATCH", "インポートの準備状態を確認できませんでした。もう一度ファイルを選択してください。");
            return preview;
        }
        catch (OperationCanceledException)
        {
            await DiscardBestEffortAsync(id).ConfigureAwait(false);
            throw;
        }
        catch (ApplicationErrorException)
        {
            await DiscardBestEffortAsync(id).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await DiscardBestEffortAsync(id).ConfigureAwait(false);
            throw new ApplicationErrorException("IMPORT_INVALID", "インポートファイルを検証できませんでした。対応する形式のファイルを選択してください。", innerException: exception);
        }
    }

    /// <inheritdoc />
    public async Task CommitImportAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidateId(preparedImportId.Value, nameof(preparedImportId));
        cancellationToken.ThrowIfCancellationRequested();
        var committed = await transactions.ExecuteAsync(token => staging.TryConsumeAndReplaceLiveDataAsync(
            preparedImportId, clock.UtcNow.ToUniversalTime(), token), cancellationToken).ConfigureAwait(false);
        if (!committed)
            throw new ApplicationErrorException("IMPORT_NOT_PREPARED", "確認済みのインポートが見つからないか、既に取り込まれています。もう一度ファイルを選択してください。");
        await DiscardBestEffortAsync(preparedImportId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DiscardImportAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidateId(preparedImportId.Value, nameof(preparedImportId));
        cancellationToken.ThrowIfCancellationRequested();
        await staging.DiscardAsync(preparedImportId, cancellationToken).ConfigureAwait(false);
    }

    private async Task DiscardBestEffortAsync(PreparedImportId preparedImportId)
    {
        try
        {
            await staging.DiscardAsync(preparedImportId, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // 本番データの確定結果や元の検証エラーを、一時領域の清掃失敗で上書きしない。
            // 残存データは次回 PrepareImportAsync の DiscardAbandonedAsync で再清掃する。
        }
    }
}
