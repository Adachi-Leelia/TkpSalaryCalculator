using System.Security.Cryptography;
using System.Text.Json;
using TkpSalaryCalculator.Application.Contracts;

namespace TkpSalaryCalculator.Application.Internal;

/// <summary>確認対象の内容を固定し、プレビュー後の変更を検出します。</summary>
internal static class ConfirmationFingerprint
{
    internal static string Create<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    internal static string ForRecords(IEnumerable<WorkRecordDto> records) =>
        Create(records.OrderBy(record => record.Id.Value).ToArray());
}
