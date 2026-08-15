using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Contracts;

/// <summary>本番データセットの外部に準備されたインポートを識別します。</summary>
/// <param name="Value">内部構造を公開しない準備済みインポート識別子。</param>
public readonly record struct PreparedImportId(Guid Value);

/// <summary>プレゼンテーション層に表示する現在のデータ転送形式を識別します。</summary>
/// <param name="Format">安定した形式識別子。</param>
/// <param name="FormatVersion">独立してバージョン管理されるデータ転送形式の番号。</param>
public sealed record DataTransferFormatDto(string Format, int FormatVersion);

/// <summary>エクスポート文書のメタデータを保持します。</summary>
/// <param name="Format">安定した文書形式識別子。</param>
/// <param name="FormatVersion">独立したエクスポート形式のバージョン。</param>
/// <param name="CreatedAtUtc">UTC での作成日時。</param>
/// <param name="AppVersion">作成元アプリケーションのバージョン。</param>
public sealed record ExportDocumentHeader(
    string Format,
    int FormatVersion,
    DateTimeOffset CreatedAtUtc,
    string AppVersion);

/// <summary>エクスポート文書の論理セクションを識別します。</summary>
public enum DataTransferSection
{
    /// <summary>文書メタデータ。</summary>
    Metadata,
    /// <summary>設定月の参照。</summary>
    SettingMonths,
    /// <summary>不変の設定スナップショットと子データ。</summary>
    SettingSnapshots,
    /// <summary>締め日ルールの履歴。</summary>
    ClosingRules,
    /// <summary>月額手当。</summary>
    MonthlyAllowances,
    /// <summary>論理定義。</summary>
    Definitions,
    /// <summary>サービスプリセット。</summary>
    ServicePresets,
    /// <summary>基本シフト。</summary>
    BasicShifts,
    /// <summary>勤務記録。</summary>
    WorkRecords,
    /// <summary>祝日カレンダーのバージョンと日付。</summary>
    Holidays,
}

/// <summary>論理ストリーミングレコードの非ジェネリック基底型を提供します。</summary>
/// <param name="Section">所属する論理セクション。</param>
/// <param name="Sequence">セクション内での 0 始まりの順序。</param>
public abstract record DataTransferRecord(DataTransferSection Section, long Sequence);

/// <summary>ストリーミング形式のエクスポートまたはインポートに含まれる、厳密に型付けされた論理レコードを保持します。</summary>
/// <typeparam name="T">形式バージョンが対応する不変の契約型。</typeparam>
/// <param name="Section">所属する論理セクション。</param>
/// <param name="Sequence">セクション内での 0 始まりの順序。</param>
/// <param name="Value">不変のレコード値。</param>
public sealed record DataTransferRecord<T>(DataTransferSection Section, long Sequence, T Value)
    : DataTransferRecord(Section, Sequence);

/// <summary>本番データを変更せずに準備した、検証済みインポートの概要を保持します。</summary>
/// <param name="Id">準備済みインポート識別子。</param>
/// <param name="FormatVersion">検証済みの形式バージョン。</param>
/// <param name="ExportCreatedAtUtc">エクスポート文書に記録された作成日時。</param>
/// <param name="SettingMonthCount">準備された設定月の件数。</param>
/// <param name="BasicShiftCount">準備された基本シフトの件数。</param>
/// <param name="WorkRecordCount">準備された勤務記録の件数。</param>
/// <param name="MonthlyAllowanceCount">準備された月額手当の件数。</param>
/// <param name="OldestSettingMonth">準備された最古の設定月。存在しない場合があります。</param>
/// <param name="LatestSettingMonth">準備された最新の設定月。存在しない場合があります。</param>
/// <param name="OldestWorkDate">準備された最古の勤務日。存在しない場合があります。</param>
/// <param name="LatestWorkDate">準備された最新の勤務日。存在しない場合があります。</param>
/// <param name="Warnings">処理を妨げない検証警告。</param>
public sealed record ImportPreviewDto(
    PreparedImportId Id,
    int FormatVersion,
    DateTimeOffset ExportCreatedAtUtc,
    long SettingMonthCount,
    long BasicShiftCount,
    long WorkRecordCount,
    long MonthlyAllowanceCount,
    YearMonth? OldestSettingMonth,
    YearMonth? LatestSettingMonth,
    DateOnly? OldestWorkDate,
    DateOnly? LatestWorkDate,
    IReadOnlyList<IssueDto> Warnings);
