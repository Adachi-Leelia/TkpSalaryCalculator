using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Services;

/// <summary>年間区分の境界と、給与期間集計からの年間累計を決定します。</summary>
public sealed class AnnualSalaryCalculator : IAnnualSalaryCalculator
{
    private const long MaximumMonthIndex = 9999L * 12 - 1;

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
        var startIndex = ToMonthIndex(selected) - monthsFromStart;
        var endIndex = startIndex + 11;
        if (startIndex < 0 || endIndex > MaximumMonthIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selected),
                selected,
                "指定した給与期間と年間締め月から求めた年間区分が、表現可能な年月の範囲を超えています。");
        }

        var start = FromMonthIndex(startIndex);
        var end = FromMonthIndex(endIndex);
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

        long? precedingMonthIndex = null;
        var uncalculatedCount = 0;
        foreach (var period in periods)
        {
            if (period?.Period is null)
            {
                throw new ArgumentException("給与期間集計にnullを含めることはできません。", nameof(periods));
            }

            var monthIndex = ToMonthIndex(period.Period.Key);
            if (precedingMonthIndex is { } preceding && monthIndex != preceding + 1)
            {
                throw new ArgumentException("給与期間集計は重複や欠落なくキー順に指定してください。", nameof(periods));
            }

            if (period.UncalculatedCount < 0)
            {
                throw new ArgumentException("未計算勤務記録数は0件以上で指定してください。", nameof(periods));
            }

            uncalculatedCount = checked(uncalculatedCount + period.UncalculatedCount);
            precedingMonthIndex = monthIndex;
        }

        return new AnnualSalaryCalculation(
            new YenAmount(MoneyMath.Sum(periods.Select(static period => period.CalculatedSubtotal.Value))),
            uncalculatedCount);
    }

    private static long ToMonthIndex(PayrollPeriodKey key) =>
        ((long)key.Value.Year - 1) * 12 + key.Value.Month - 1;

    private static PayrollPeriodKey FromMonthIndex(long monthIndex) => new(
        new YearMonth((int)(monthIndex / 12) + 1, (int)(monthIndex % 12) + 1));
}
