using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Services;

/// <summary>年間区分の境界と、給与期間集計からの年間累計を決定します。</summary>
public sealed class AnnualSalaryCalculator : IAnnualSalaryCalculator
{
    /// <inheritdoc />
    public AnnualSalaryPeriodRange GetPeriodRange(
        PayrollPeriodKey selected,
        AnnualClosingMonth closingMonth)
    {
        DomainValueGuard.ValidPayrollPeriodKey(selected, nameof(selected));
        if (closingMonth.Value is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(closingMonth), "年間締め月は1月から12月の範囲で指定してください。");
        }

        var firstMonth = closingMonth.Value == 12 ? 1 : closingMonth.Value + 1;
        var monthsFromStart = (selected.Value.Month - firstMonth + 12) % 12;
        var start = new PayrollPeriodKey(selected.Value.AddMonths(-monthsFromStart));
        var end = new PayrollPeriodKey(start.Value.AddMonths(11));
        return new AnnualSalaryPeriodRange(start, end, selected);
    }

    /// <inheritdoc />
    public AnnualSalaryCalculation Aggregate(
        IReadOnlyList<PayrollPeriodSalaryCalculation> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);
        if (periods.Count == 0)
        {
            throw new ArgumentException("年間累計には1件以上の給与期間集計を指定してください。", nameof(periods));
        }

        var normalized = new List<PayrollPeriodSalaryCalculation>(periods.Count);
        PayrollPeriodKey? precedingKey = null;
        foreach (var period in periods)
        {
            if (period is null)
            {
                throw new ArgumentException("給与期間集計にnullを含めることはできません。", nameof(periods));
            }

            if (precedingKey is { } preceding &&
                period.Period.Key.Value != preceding.Value.AddMonths(1))
            {
                throw new ArgumentException("給与期間集計は重複や欠落なくキー順に指定してください。", nameof(periods));
            }

            var recalculated = new SalaryCalculator().AggregatePeriod(
                period.Period,
                period.Days,
                period.Allowances);
            if (recalculated.BasePaySubtotal != period.BasePaySubtotal ||
                recalculated.PremiumSubtotal != period.PremiumSubtotal ||
                recalculated.CountBonusSubtotal != period.CountBonusSubtotal ||
                recalculated.AllowanceSubtotal != period.AllowanceSubtotal ||
                recalculated.CalculatedSubtotal != period.CalculatedSubtotal ||
                recalculated.UncalculatedCount != period.UncalculatedCount)
            {
                throw new ArgumentException("給与期間集計が日別結果と月額手当の内訳に一致しません。", nameof(periods));
            }

            normalized.Add(recalculated);
            precedingKey = period.Period.Key;
        }

        var uncalculatedCount = 0;
        foreach (var period in normalized)
        {
            uncalculatedCount = checked(uncalculatedCount + period.UncalculatedCount);
        }

        return new AnnualSalaryCalculation(
            new YenAmount(MoneyMath.Sum(normalized.Select(static period => period.CalculatedSubtotal.Value))),
            uncalculatedCount);
    }
}
