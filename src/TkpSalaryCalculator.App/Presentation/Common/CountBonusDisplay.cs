using TkpSalaryCalculator.Domain.Contracts;

namespace TkpSalaryCalculator.App.Presentation.Common;

public static class CountBonusDisplay
{
    public static string Selection(bool enabled) => enabled ? "加算する" : "加算しない";

    public static string Summary(bool enabled, WorkSalaryCalculation? calculation, JapaneseDisplayFormatter formatter)
    {
        var selection = $"件数手当：{Selection(enabled)}";
        if (calculation?.Status != SalaryCalculationStatus.Calculated) return selection;
        if (!enabled) return selection + "（0円）";
        return calculation.CountBonuses.Count == 0
            ? selection + "（適用なし）（0円）"
            : "訪問の件数加算: " + string.Join("、", calculation.CountBonuses.Select(bonus =>
                $"{bonus.DisplayName} {formatter.Money(bonus.Amount)}"));
    }
}
