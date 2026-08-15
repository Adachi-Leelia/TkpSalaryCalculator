using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Internal;

internal static class ClosingRuleHistorySupport
{
    public static readonly PayrollPeriodKey Baseline = new(new YearMonth(1, 1));

    public static IReadOnlyList<ClosingRule> ForCalculation(IReadOnlyList<ClosingRule> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return history.OrderBy(x => x.EffectiveFrom.Value).ToArray();
    }

    public static IReadOnlyList<ClosingRule> WithReplacementForCalculation(
        IReadOnlyList<ClosingRule> history,
        ClosingRule replacement)
    {
        var persistedReplacement = history.Count == 0
            ? new ClosingRule(replacement.Id, Baseline, replacement.ClosingDay)
            : replacement;
        return ForCalculation(history.Where(x => x.EffectiveFrom != persistedReplacement.EffectiveFrom)
            .Append(persistedReplacement).ToArray());
    }
}
