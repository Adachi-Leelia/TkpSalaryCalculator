using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Contracts;

/// <summary>1 暦月について表示する設定を保持します。</summary>
/// <param name="YearMonth">対象の暦月。</param>
/// <param name="Snapshot">その月が現在参照している不変スナップショット。</param>
public sealed record MonthSettingsDto(YearMonth YearMonth, SettingSnapshot Snapshot);

/// <summary>複製した設定スナップショットの完全な置換内容を保持します。</summary>
/// <param name="Services">置換後のサービス行。</param>
/// <param name="TimeCategories">置換後の時間区分行。</param>
/// <param name="Rates">置換後の単価行。</param>
/// <param name="Premiums">置換後の割増行。</param>
/// <param name="CountBonuses">置換後の件数加算行。</param>
public sealed record SettingSnapshotReplacementDto(
    IReadOnlyList<SnapshotService> Services,
    IReadOnlyList<SnapshotTimeCategory> TimeCategories,
    IReadOnlyList<SnapshotRate> Rates,
    IReadOnlyList<SnapshotPremium> Premiums,
    IReadOnlyList<SnapshotCountBonus> CountBonuses);

/// <summary>1 か月分のスナップショットを置換した場合の予測結果を保持します。</summary>
/// <param name="TargetMonth">参照先が変更される月。</param>
/// <param name="ConfirmationToken">確認後の変更を検出するため、確定時にそのまま渡すトークン。</param>
/// <param name="AffectedWorkRecordCount">結果が変わる既存勤務記録の件数。</param>
/// <param name="CurrentCalculatedSubtotal">現在の計算済み小計。</param>
/// <param name="ReplacementCalculatedSubtotal">置換後に予測される計算済み小計。</param>
/// <param name="ResultingUncalculatedCount">置換後に予測される未計算記録数。</param>
/// <param name="Issues">確定前に解決する必要がある検証上の問題。</param>
public sealed record SettingReplacementPreviewDto(
    YearMonth TargetMonth,
    SettingReplacementConfirmationToken ConfirmationToken,
    int AffectedWorkRecordCount,
    YenAmount CurrentCalculatedSubtotal,
    YenAmount ReplacementCalculatedSubtotal,
    int ResultingUncalculatedCount,
    IReadOnlyList<IssueDto> Issues);

/// <summary>設定置換の確認後に対象設定または勤務が変化していないことを検証するトークンです。</summary>
/// <param name="TargetMonth">確認した対象年月。</param>
/// <param name="TargetSnapshotId">確認時に対象月へ適用されていたスナップショット。</param>
/// <param name="SourceSnapshotId">前月コピーで確認したコピー元。通常置換では <see langword="null"/>。</param>
/// <param name="WorkRecordsFingerprint">対象月の勤務内容を表す不透明な指紋。</param>
/// <param name="ReplacementFingerprint">確認表示した完全な置換内容を表す不透明な指紋。</param>
/// <param name="HolidayCalendarVersionId">確認計算に使用した祝日データ版。</param>
public sealed record SettingReplacementConfirmationToken(
    YearMonth TargetMonth,
    SettingSnapshotId TargetSnapshotId,
    SettingSnapshotId? SourceSnapshotId,
    string WorkRecordsFingerprint,
    string ReplacementFingerprint,
    HolidayCalendarVersionId HolidayCalendarVersionId);

/// <summary>指定した給与期間の月から有効になる締め日ルールの置換内容を保持します。</summary>
/// <param name="EffectiveFrom">最初に影響を受ける給与期間。</param>
/// <param name="ClosingDay">1 日から 31 日までの日付。月末締めの場合は <see langword="null"/>。</param>
public sealed record ReplaceClosingRuleCommand(PayrollPeriodKey EffectiveFrom, int? ClosingDay);

/// <summary>指定した給与期間に有効な締め日ルールを表します。</summary>
/// <param name="PayrollPeriodKey">指定された給与期間。</param>
/// <param name="RuleId">有効な履歴項目。</param>
/// <param name="EffectiveFrom">履歴項目が適用される最初の期間。</param>
/// <param name="ClosingDay">設定された 1 日から 31 日までの日付。月末の場合は <see langword="null"/>。</param>
public sealed record EffectiveClosingRuleDto(
    PayrollPeriodKey PayrollPeriodKey,
    ClosingRuleId RuleId,
    PayrollPeriodKey EffectiveFrom,
    int? ClosingDay);

/// <summary>締め日履歴を置換した場合に最初に変化する給与期間を表示します。</summary>
/// <param name="EffectiveFrom">変更が始まる給与期間月。</param>
/// <param name="CurrentPeriod">現在の履歴による期間。履歴が不足する場合は存在しません。</param>
/// <param name="ReplacementPeriod">置換後の開始日と終了日。</param>
/// <param name="ConfirmationToken">確定時に履歴の競合を検出する確認トークン。</param>
public sealed record ClosingRuleReplacementPreviewDto(
    PayrollPeriodKey EffectiveFrom,
    PayrollPeriod? CurrentPeriod,
    PayrollPeriod ReplacementPeriod,
    ClosingRuleReplacementConfirmationToken ConfirmationToken);

/// <summary>締め日変更の確認後に履歴が変化していないことを検証するトークンです。</summary>
/// <param name="EffectiveFrom">確認対象の適用開始月。</param>
/// <param name="ClosingDay">確認表示した置換後の締め日。</param>
/// <param name="HistoryVersion">永続化ポートが発行した締め日履歴全体の版。</param>
public sealed record ClosingRuleReplacementConfirmationToken(
    PayrollPeriodKey EffectiveFrom,
    int? ClosingDay,
    ClosingRuleHistoryVersion HistoryVersion);

/// <summary>締め日履歴全体の不透明な永続版を表します。</summary>
/// <param name="Value">同じ履歴に対して安定し、変更時に変わる不透明な値。</param>
public sealed record ClosingRuleHistoryVersion(string Value);

/// <summary>同じ読取時点の締め日履歴と永続版を保持します。</summary>
/// <param name="Rules">適用開始月順に解釈する締め日履歴。</param>
/// <param name="Version">この履歴全体の不透明な版。</param>
public sealed record ClosingRuleHistorySnapshot(
    IReadOnlyList<ClosingRule> Rules,
    ClosingRuleHistoryVersion Version);

/// <summary>年間給与見込み累計の現在設定を保持します。</summary>
/// <param name="ClosingMonth">年間区分の最終月。</param>
public sealed record AnnualSummarySettingDto(AnnualClosingMonth ClosingMonth);

/// <summary>年間給与見込み累計の設定保存内容を保持します。</summary>
/// <param name="ClosingMonth">1 月から 12 月までの年間締め月。</param>
public sealed record SaveAnnualSummarySettingCommand(int ClosingMonth);

/// <summary>月額手当の入力内容を保持します。</summary>
/// <param name="Id">既存の識別子。新規手当の場合は <see langword="null"/>。</param>
/// <param name="PayrollPeriodKey">対象の給与期間。</param>
/// <param name="DisplayName">利用者向けの名称。</param>
/// <param name="Amount">円単位の金額。</param>
public sealed record SaveMonthlyAllowanceCommand(
    MonthlyAllowanceId? Id,
    PayrollPeriodKey PayrollPeriodKey,
    string DisplayName,
    YenAmount Amount);
