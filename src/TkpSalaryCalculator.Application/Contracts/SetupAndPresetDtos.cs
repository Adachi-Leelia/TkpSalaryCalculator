using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Contracts;

/// <summary>必須となる最小限の初期設定の進捗を表します。</summary>
public enum InitialSetupStatus
{
    /// <summary>初期設定が開始されていません。</summary>
    NotStarted,
    /// <summary>初期設定が途中まで保存されており、再開できます。</summary>
    InProgress,
    /// <summary>最低限必要な給与設定がすべて有効です。</summary>
    Completed,
}

/// <summary>再開可能な初期設定の状態を保持します。</summary>
/// <param name="Status">初期設定全体の状態。</param>
/// <param name="Step">安定した現在のステップ識別子。存在しない場合があります。</param>
/// <param name="Issues">不足している必須要件。</param>
public sealed record InitialSetupStateDto(
    InitialSetupStatus Status,
    string? Step,
    IReadOnlyList<IssueDto> Issues);

/// <summary>単一行で保存するアプリケーションメタデータの契約を表します。</summary>
/// <param name="InitialSetupStatus">保存済みの初期設定状態。</param>
/// <param name="InitialSetupStep">初期設定進行中の、再開に使用する安定したステップ。</param>
/// <param name="InitialSnapshotId">初期設定スナップショット。まだ確立されていない場合があります。</param>
/// <param name="ExportFormatVersion">現在のエクスポート形式バージョン。</param>
/// <param name="LastExportedAtUtc">直近でエクスポートに成功した日時。</param>
/// <param name="LastDataChangedAtUtc">直近で確定した設定または勤務データの変更日時。</param>
/// <param name="BackupReminderDeferredUntilDate">バックアップ通知を非表示にする期限を表す端末現地日付。</param>
public sealed record AppMetadata(
    InitialSetupStatus InitialSetupStatus,
    string? InitialSetupStep,
    SettingSnapshotId? InitialSnapshotId,
    int ExportFormatVersion,
    DateTimeOffset? LastExportedAtUtc,
    DateTimeOffset? LastDataChangedAtUtc,
    DateOnly? BackupReminderDeferredUntilDate);

/// <summary>プレゼンテーション層向けに計算したバックアップ通知の状態を保持します。</summary>
/// <param name="EvaluatedOnLocalDate">日付に基づく判定に使用した端末現地日付。</param>
/// <param name="ShouldShow">現在通知を表示する必要があるかどうか。</param>
/// <param name="HasWorkRecords">バックアップ対象となる勤務データが存在するかどうか。</param>
/// <param name="LastExportedAtUtc">直近でエクスポートに成功した日時。</param>
/// <param name="LastDataChangedAtUtc">直近で確定したデータ変更日時。</param>
/// <param name="DeferredUntilDate">通知を非表示にする期限を表す端末現地日付。</param>
public sealed record BackupReminderStateDto(
    DateOnly EvaluatedOnLocalDate,
    bool ShouldShow,
    bool HasWorkRecords,
    DateTimeOffset? LastExportedAtUtc,
    DateTimeOffset? LastDataChangedAtUtc,
    DateOnly? DeferredUntilDate);

/// <summary>入力補助にのみ使用する現在のサービスプリセットを表します。</summary>
/// <param name="Id">プリセット識別子。</param>
/// <param name="DisplayName">利用者向けの名称。</param>
/// <param name="ServiceId">勤務入力へコピーする具体的なサービス。</param>
/// <param name="TimeCategoryId">勤務入力へコピーする具体的な時間区分。存在しない場合があります。</param>
/// <param name="DefaultWorkMinutes">既定の勤務時間。</param>
/// <param name="DisplayOrder">候補の表示順。</param>
/// <param name="IsEnabled">新規入力用の候補として提示するかどうか。</param>
public sealed record ServicePresetDto(
    ServicePresetId Id,
    string DisplayName,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkMinutes DefaultWorkMinutes,
    DisplayOrder DisplayOrder,
    bool IsEnabled);

/// <summary>サービスプリセットを作成または置換するための入力内容を保持します。</summary>
/// <param name="Id">既存の識別子。新規プリセットの場合は <see langword="null"/>。</param>
/// <param name="DisplayName">利用者向けの名称。</param>
/// <param name="ServiceId">具体的なサービス。</param>
/// <param name="TimeCategoryId">具体的な時間区分。存在しない場合があります。</param>
/// <param name="DefaultWorkMinutes">既定の勤務時間。</param>
/// <param name="DisplayOrder">候補の表示順。</param>
/// <param name="IsEnabled">入力用の候補として提示するかどうか。</param>
public sealed record SaveServicePresetCommand(
    ServicePresetId? Id,
    string DisplayName,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkMinutes DefaultWorkMinutes,
    DisplayOrder DisplayOrder,
    bool IsEnabled);
