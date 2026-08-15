using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Contracts;

/// <summary>正規化された不変の入力から、副作用なく勤務記録を計算します。</summary>
public interface ISalaryCalculator
{
    /// <summary>指定された完全な設定スナップショットと祝日スナップショットを使用して勤務記録を 1 件計算します。</summary>
    /// <param name="request">正規化済みの計算要求。</param>
    /// <returns>計算済みの内訳、または明示的な設定不足の理由。</returns>
    WorkSalaryCalculation Calculate(WorkSalaryCalculationRequest request);

    /// <summary>現地勤務日 1 日分の計算済み記録と未計算記録を集計します。</summary>
    /// <param name="workDate">現地勤務日。</param>
    /// <param name="records">個々の記録結果。</param>
    /// <returns>決定論的な基本給、割増、件数加算、合計の各小計、および未計算件数。</returns>
    DailySalaryCalculation AggregateDay(
        DateOnly workDate,
        IReadOnlyList<WorkSalaryCalculation> records);

    /// <summary>1 給与期間の日別結果と直接適用する手当を集計します。</summary>
    /// <param name="period">両端の日付を含む期間。</param>
    /// <param name="days">期間内の日別結果。</param>
    /// <param name="allowances">期間に 1 回適用される手当。</param>
    /// <returns>決定論的な基本給、割増、件数加算、手当、合計の各小計、および未計算件数。</returns>
    PayrollPeriodSalaryCalculation AggregatePeriod(
        PayrollPeriod period,
        IReadOnlyList<DailySalaryCalculation> days,
        IReadOnlyList<MonthlyAllowance> allowances);
}

/// <summary>保存処理や時計に依存せず、給与期間の境界を計算します。</summary>
public interface IPayrollPeriodCalculator
{
    /// <summary>指定されたキーで識別される、両端の日付を含む期間を計算します。</summary>
    /// <param name="key">給与期間キー。</param>
    /// <param name="closingRules">指定キーとその直前のキーを解決するために必要なすべての締め日ルール。</param>
    /// <returns>決定論的な給与期間。</returns>
    PayrollPeriod GetPeriod(PayrollPeriodKey key, IReadOnlyList<ClosingRule> closingRules);

    /// <summary>現地勤務日を含む給与期間を検索します。</summary>
    /// <param name="workDate">現地勤務日。</param>
    /// <param name="closingRules">締め日ルールの履歴。</param>
    /// <returns>両端を含む境界内に指定日が含まれる一意の期間。</returns>
    PayrollPeriod FindPeriod(DateOnly workDate, IReadOnlyList<ClosingRule> closingRules);
}

/// <summary>1 件の勤務記録を計算するために必要な、副作用のない入力をすべて保持します。</summary>
/// <param name="WorkRecord">正規化済みの勤務記録。</param>
/// <param name="SettingSnapshot">勤務日の暦月から選択されたスナップショット。</param>
/// <param name="HolidayCalendar">設定スナップショットが参照する祝日カレンダー。</param>
public sealed record WorkSalaryCalculationRequest(
    WorkRecord WorkRecord,
    SettingSnapshot SettingSnapshot,
    HolidayCalendar HolidayCalendar);

/// <summary>給与結果を計算できたかどうかを表します。</summary>
public enum SalaryCalculationStatus
{
    /// <summary>必要な設定がすべて存在し、結果が完全です。</summary>
    Calculated,

    /// <summary>入力は有効ですが、必要な計算設定が不足しています。</summary>
    Uncalculated,
}

/// <summary>金額を推測せず、不足している計算要件を識別します。</summary>
/// <param name="Code">機械判読可能な安定した理由コード。</param>
/// <param name="RelatedId">任意の関連論理識別子。</param>
public sealed record MissingCalculationRequirement(string Code, Guid? RelatedId);

/// <summary>適用された割増の 1 行を保持します。</summary>
/// <param name="Rule">サービス、日付、祝日、曜日、時間条件の判定に使用した完全で不変の割増ルール。</param>
/// <param name="ApplicableMinutes">対象となる分数。</param>
/// <param name="Amount">円単位に丸めた割増額。</param>
public sealed record AppliedPremium(
    SnapshotPremium Rule,
    WorkMinutes ApplicableMinutes,
    YenAmount Amount);

/// <summary>適用された件数加算の 1 行を保持します。</summary>
/// <param name="CountBonusId">適用された加算の識別子。</param>
/// <param name="DisplayName">スナップショットに固定された加算名。</param>
/// <param name="Amount">円単位の加算額。</param>
public sealed record AppliedCountBonus(CountBonusId CountBonusId, string DisplayName, YenAmount Amount);

/// <summary>1 件の勤務記録に対する決定論的な結果を保持します。</summary>
/// <param name="WorkRecordId">計算対象の記録。</param>
/// <param name="Status">結果が完全かどうか。</param>
/// <param name="AppliedRate">選択された単価。未計算の場合は <see langword="null"/>。</param>
/// <param name="BasePay">丸め済みの基本給。未計算の場合は <see langword="null"/>。</param>
/// <param name="Premiums">適用された個別の割増。</param>
/// <param name="CountBonuses">適用された個別の件数加算。</param>
/// <param name="Total">記録の合計。未計算の場合は <see langword="null"/>。</param>
/// <param name="MissingRequirements">計算できなかった明示的な理由。</param>
public sealed record WorkSalaryCalculation(
    WorkRecordId WorkRecordId,
    SalaryCalculationStatus Status,
    SnapshotRate? AppliedRate,
    YenAmount? BasePay,
    IReadOnlyList<AppliedPremium> Premiums,
    IReadOnlyList<AppliedCountBonus> CountBonuses,
    YenAmount? Total,
    IReadOnlyList<MissingCalculationRequirement> MissingRequirements);

/// <summary>副作用のない日別集計結果を保持します。</summary>
/// <param name="WorkDate">現地勤務日。</param>
/// <param name="Records">個々の記録の計算結果。</param>
/// <param name="BasePaySubtotal">計算済み記録の基本給小計。</param>
/// <param name="PremiumSubtotal">計算済み記録の割増小計。</param>
/// <param name="CountBonusSubtotal">計算済み記録の件数加算小計。</param>
/// <param name="CalculatedSubtotal">完全に計算された記録の小計。</param>
/// <param name="UncalculatedCount">不完全な記録の件数。</param>
public sealed record DailySalaryCalculation(
    DateOnly WorkDate,
    IReadOnlyList<WorkSalaryCalculation> Records,
    YenAmount BasePaySubtotal,
    YenAmount PremiumSubtotal,
    YenAmount CountBonusSubtotal,
    YenAmount CalculatedSubtotal,
    int UncalculatedCount);

/// <summary>副作用のない給与期間集計結果を保持します。</summary>
/// <param name="Period">両端の日付を含む給与期間。</param>
/// <param name="Days">日別集計結果。</param>
/// <param name="Allowances">期間に直接適用する手当。</param>
/// <param name="BasePaySubtotal">計算済み記録の基本給小計。</param>
/// <param name="PremiumSubtotal">計算済み記録の割増小計。</param>
/// <param name="CountBonusSubtotal">計算済み記録の件数加算小計。</param>
/// <param name="AllowanceSubtotal">給与期間に直接適用する手当の小計。</param>
/// <param name="CalculatedSubtotal">計算済み記録の小計と手当の合計。</param>
/// <param name="UncalculatedCount">不完全な勤務記録の件数。</param>
public sealed record PayrollPeriodSalaryCalculation(
    PayrollPeriod Period,
    IReadOnlyList<DailySalaryCalculation> Days,
    IReadOnlyList<MonthlyAllowance> Allowances,
    YenAmount BasePaySubtotal,
    YenAmount PremiumSubtotal,
    YenAmount CountBonusSubtotal,
    YenAmount AllowanceSubtotal,
    YenAmount CalculatedSubtotal,
    int UncalculatedCount);
