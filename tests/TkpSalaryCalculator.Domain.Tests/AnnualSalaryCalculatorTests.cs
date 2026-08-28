using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Tests;

public sealed class AnnualSalaryCalculatorTests
{
    private readonly AnnualSalaryCalculator annual = new();
    private readonly SalaryCalculator salary = new();

    public static TheoryData<int> ClosingMonths => new()
    {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12,
    };

    [Theory]
    [MemberData(nameof(ClosingMonths))]
    public void AllClosingMonthsKeepStartMiddleAndEndInTheSameAnnualPeriod(int closingMonth)
    {
        var expectedEnd = new YearMonth(2027, closingMonth);
        var expectedStart = expectedEnd.AddMonths(-11);
        var selectedMonths = new[] { expectedStart, expectedStart.AddMonths(5), expectedEnd };

        foreach (var selectedMonth in selectedMonths)
        {
            var range = annual.GetPeriodRange(
                new PayrollPeriodKey(selectedMonth),
                new AnnualClosingMonth(closingMonth));

            Assert.Equal(expectedStart, range.Start.Value);
            Assert.Equal(expectedEnd, range.End.Value);
            Assert.Equal(selectedMonth, range.AccumulationEnd.Value);
        }
    }

    [Fact]
    public void DefaultClosingMonthIsDecemberAndJanuaryStartsANewPeriod()
    {
        Assert.Equal(12, AnnualClosingMonth.Default.Value);

        var december = annual.GetPeriodRange(Key(2026, 12), AnnualClosingMonth.Default);
        var january = annual.GetPeriodRange(Key(2027, 1), AnnualClosingMonth.Default);

        Assert.Equal(new YearMonth(2026, 1), december.Start.Value);
        Assert.Equal(new YearMonth(2026, 12), december.End.Value);
        Assert.Equal(new YearMonth(2027, 1), january.Start.Value);
        Assert.Equal(new YearMonth(2027, 12), january.End.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void ClosingMonthOutsideOneThroughTwelveIsRejected(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnualClosingMonth(value));
    }

    [Fact]
    public void AggregateAddsPeriodTotalsAndUncalculatedCounts()
    {
        var january = PeriodCalculation(2026, 1, 100, uncalculatedCount: 1);
        var february = PeriodCalculation(2026, 2, 250, uncalculatedCount: 2);

        var result = annual.Aggregate([january, february]);

        Assert.Equal(350, result.CalculatedSubtotal.Value);
        Assert.Equal(3, result.UncalculatedCount);
    }

    [Fact]
    public void AggregateDetectsOverflowAndRejectsMissingOrUnorderedPeriods()
    {
        var january = PeriodCalculation(2026, 1, long.MaxValue);
        var february = PeriodCalculation(2026, 2, 1);
        var march = PeriodCalculation(2026, 3, 1);

        Assert.Throws<OverflowException>(() => annual.Aggregate([january, february]));
        Assert.Throws<ArgumentException>(() => annual.Aggregate([january, march]));
        Assert.Throws<ArgumentException>(() => annual.Aggregate([february, january]));
        Assert.Throws<ArgumentException>(() => annual.Aggregate([]));
    }

    private PayrollPeriodSalaryCalculation PeriodCalculation(
        int year,
        int month,
        long amount,
        int uncalculatedCount = 0)
    {
        var key = Key(year, month);
        var period = new PayrollPeriod(
            key,
            new DateOnly(year, month, 1),
            new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
        var allowances = amount == 0
            ? Array.Empty<MonthlyAllowance>()
            : [new MonthlyAllowance(new MonthlyAllowanceId(Guid.NewGuid()), key, "手当", new YenAmount(amount))];
        var days = new List<DailySalaryCalculation>();
        if (uncalculatedCount > 0)
        {
            var records = Enumerable.Range(0, uncalculatedCount)
                .Select(_ => new WorkSalaryCalculation(
                    new WorkRecordId(Guid.NewGuid()),
                    SalaryCalculationStatus.Uncalculated,
                    null,
                    null,
                    [],
                    [],
                    null,
                    [new MissingCalculationRequirement("MISSING_RATE", null)]))
                .ToArray();
            days.Add(salary.AggregateDay(period.StartDate, records));
        }

        return salary.AggregatePeriod(period, days, allowances);
    }

    private static PayrollPeriodKey Key(int year, int month) => new(new YearMonth(year, month));
}
