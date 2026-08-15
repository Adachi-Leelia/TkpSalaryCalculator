using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Services;

/// <summary>締め日履歴から保存値を持たない給与期間を決定的に算出します。</summary>
public sealed class PayrollPeriodCalculator : IPayrollPeriodCalculator
{
    /// <inheritdoc />
    public PayrollPeriod GetPeriod(PayrollPeriodKey key, IReadOnlyList<ClosingRule> closingRules)
    {
        DomainValueGuard.ValidPayrollPeriodKey(key, nameof(key));
        var rules = ValidateAndCopyRules(closingRules);
        var previousKey = new PayrollPeriodKey(key.Value.AddMonths(-1));
        var previousEnd = GetEndDate(previousKey, rules);
        var end = GetEndDate(key, rules);
        return new PayrollPeriod(key, previousEnd.AddDays(1), end);
    }

    /// <inheritdoc />
    public PayrollPeriod FindPeriod(DateOnly workDate, IReadOnlyList<ClosingRule> closingRules)
    {
        var rules = ValidateAndCopyRules(closingRules);
        var key = new PayrollPeriodKey(new YearMonth(workDate.Year, workDate.Month));
        if (workDate > GetEndDate(key, rules))
        {
            key = new PayrollPeriodKey(key.Value.AddMonths(1));
        }

        var period = GetPeriod(key, rules);
        if (!period.Contains(workDate))
        {
            throw new InvalidOperationException("勤務日を含む給与期間を締め日履歴から決定できませんでした。");
        }

        return period;
    }

    private static ClosingRule[] ValidateAndCopyRules(IReadOnlyList<ClosingRule> closingRules)
    {
        ArgumentNullException.ThrowIfNull(closingRules);
        var rules = closingRules.ToArray();
        if (rules.Any(static rule => rule is null))
        {
            throw new ArgumentException("締め日履歴にnullを含めることはできません。", nameof(closingRules));
        }

        var ids = new HashSet<ClosingRuleId>();
        var effectiveMonths = new HashSet<PayrollPeriodKey>();
        foreach (var rule in rules)
        {
            DomainIdGuard.NotEmpty(rule.Id.Value, nameof(closingRules));
            DomainValueGuard.ValidPayrollPeriodKey(rule.EffectiveFrom, nameof(closingRules));
            if (rule.ClosingDay is < 1 or > 31)
            {
                throw new ArgumentException("締め日は1から31、または月末締めを表すnullで指定してください。", nameof(closingRules));
            }

            if (!ids.Add(rule.Id))
            {
                throw new ArgumentException("締め日履歴IDが重複しています。", nameof(closingRules));
            }

            if (!effectiveMonths.Add(rule.EffectiveFrom))
            {
                throw new ArgumentException("同じ適用開始年月の締め日履歴が重複しています。", nameof(closingRules));
            }
        }

        Array.Sort(rules, static (left, right) => left.EffectiveFrom.Value.CompareTo(right.EffectiveFrom.Value));
        return rules;
    }

    private static DateOnly GetEndDate(PayrollPeriodKey key, IReadOnlyList<ClosingRule> rules)
    {
        var rule = rules.LastOrDefault(candidate => candidate.EffectiveFrom.Value.CompareTo(key.Value) <= 0) ??
                   throw new ArgumentException(
                       $"給与期間{key.Value.Year:D4}-{key.Value.Month:D2}に適用できる締め日履歴がありません。",
                       nameof(rules));

        var daysInMonth = DateTime.DaysInMonth(key.Value.Year, key.Value.Month);
        var closingDay = rule.ClosingDay is null
            ? daysInMonth
            : Math.Min(rule.ClosingDay.Value, daysInMonth);
        return new DateOnly(key.Value.Year, key.Value.Month, closingDay);
    }
}
