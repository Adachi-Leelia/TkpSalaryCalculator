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
/// <param name="AffectedWorkRecordCount">結果が変わる既存勤務記録の件数。</param>
/// <param name="CurrentCalculatedSubtotal">現在の計算済み小計。</param>
/// <param name="ReplacementCalculatedSubtotal">置換後に予測される計算済み小計。</param>
/// <param name="ResultingUncalculatedCount">置換後に予測される未計算記録数。</param>
/// <param name="Issues">確定前に解決する必要がある検証上の問題。</param>
public sealed record SettingReplacementPreviewDto(
    YearMonth TargetMonth,
    int AffectedWorkRecordCount,
    YenAmount CurrentCalculatedSubtotal,
    YenAmount ReplacementCalculatedSubtotal,
    int ResultingUncalculatedCount,
    IReadOnlyList<IssueDto> Issues);

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
