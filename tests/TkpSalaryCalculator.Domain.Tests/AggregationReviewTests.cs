using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Tests;

public sealed class AggregationReviewTests
{
    private readonly SalaryCalculator calculator = new();

    [Fact]
    public void WorkTotalOverflowIsDetected()
    {
        var snapshot = TestData.Snapshot(
            TestData.Rate(RateType.FixedPerRecord, long.MaxValue),
            bonuses: new[] { TestData.CountBonus(1) });

        Assert.Throws<OverflowException>(() => calculator.Calculate(
            TestData.Request(TestData.WorkRecord(30), snapshot)));
    }

    [Fact]
    public void DailyBasePaySubtotalOverflowIsDetected()
    {
        var records = new[]
        {
            Calculated(basePay: long.MaxValue),
            Calculated(basePay: long.MaxValue),
        };

        Assert.Throws<OverflowException>(() => calculator.AggregateDay(new DateOnly(2026, 8, 15), records));
    }

    [Fact]
    public void DailyPremiumSubtotalOverflowIsDetected()
    {
        var records = new[]
        {
            Calculated(premiums: new[] { long.MaxValue }),
            Calculated(premiums: new[] { long.MaxValue }),
        };

        Assert.Throws<OverflowException>(() => calculator.AggregateDay(new DateOnly(2026, 8, 15), records));
    }

    [Fact]
    public void DailyCountBonusSubtotalOverflowIsDetected()
    {
        var records = new[]
        {
            Calculated(bonuses: new[] { long.MaxValue }),
            Calculated(bonuses: new[] { long.MaxValue }),
        };

        Assert.Throws<OverflowException>(() => calculator.AggregateDay(new DateOnly(2026, 8, 15), records));
    }

    [Fact]
    public void DailyCalculatedSubtotalOverflowIsDetectedIndependently()
    {
        var records = new[]
        {
            Calculated(basePay: long.MaxValue),
            Calculated(premiums: new[] { 1L }),
        };

        Assert.Throws<OverflowException>(() => calculator.AggregateDay(new DateOnly(2026, 8, 15), records));
    }

    [Fact]
    public void PeriodBasePaySubtotalOverflowIsDetected()
    {
        var period = Period();
        var days = new[]
        {
            calculator.AggregateDay(new DateOnly(2026, 8, 1), new[] { Calculated(basePay: long.MaxValue) }),
            calculator.AggregateDay(new DateOnly(2026, 8, 2), new[] { Calculated(basePay: long.MaxValue) }),
        };

        Assert.Throws<OverflowException>(() => calculator.AggregatePeriod(period, days, Array.Empty<MonthlyAllowance>()));
    }

    [Fact]
    public void PeriodPremiumSubtotalOverflowIsDetected()
    {
        var period = Period();
        var days = new[]
        {
            calculator.AggregateDay(new DateOnly(2026, 8, 1), new[] { Calculated(premiums: new[] { long.MaxValue }) }),
            calculator.AggregateDay(new DateOnly(2026, 8, 2), new[] { Calculated(premiums: new[] { long.MaxValue }) }),
        };

        Assert.Throws<OverflowException>(() => calculator.AggregatePeriod(period, days, Array.Empty<MonthlyAllowance>()));
    }

    [Fact]
    public void PeriodCountBonusSubtotalOverflowIsDetected()
    {
        var period = Period();
        var days = new[]
        {
            calculator.AggregateDay(new DateOnly(2026, 8, 1), new[] { Calculated(bonuses: new[] { long.MaxValue }) }),
            calculator.AggregateDay(new DateOnly(2026, 8, 2), new[] { Calculated(bonuses: new[] { long.MaxValue }) }),
        };

        Assert.Throws<OverflowException>(() => calculator.AggregatePeriod(period, days, Array.Empty<MonthlyAllowance>()));
    }

    [Fact]
    public void AllowanceSubtotalOverflowIsDetected()
    {
        var period = Period();
        var allowances = new[]
        {
            Allowance(period.Key, long.MaxValue),
            Allowance(period.Key, long.MaxValue),
        };

        Assert.Throws<OverflowException>(() => calculator.AggregatePeriod(period, Array.Empty<DailySalaryCalculation>(), allowances));
    }

    [Fact]
    public void PeriodCalculatedSubtotalOverflowIsDetected()
    {
        var period = Period();
        var day = calculator.AggregateDay(
            new DateOnly(2026, 8, 1),
            new[] { Calculated(basePay: long.MaxValue) });

        Assert.Throws<OverflowException>(() => calculator.AggregatePeriod(
            period,
            new[] { day },
            new[] { Allowance(period.Key, 1) }));
    }

    [Fact]
    public void NoAllowanceProducesZeroAndMultipleAllowancesAreAddedOnceEach()
    {
        var period = Period();
        var withoutAllowance = calculator.AggregatePeriod(
            period,
            Array.Empty<DailySalaryCalculation>(),
            Array.Empty<MonthlyAllowance>());
        var withAllowances = calculator.AggregatePeriod(
            period,
            Array.Empty<DailySalaryCalculation>(),
            new[] { Allowance(period.Key, 100), Allowance(period.Key, 200) });

        Assert.Equal(0, withoutAllowance.AllowanceSubtotal.Value);
        Assert.Equal(0, withoutAllowance.CalculatedSubtotal.Value);
        Assert.Equal(300, withAllowances.AllowanceSubtotal.Value);
        Assert.Equal(300, withAllowances.CalculatedSubtotal.Value);
    }

    [Fact]
    public void CalculatedAndUncalculatedRecordsKeepPartialSubtotalAndCount()
    {
        var records = new[] { Calculated(basePay: 100), Uncalculated() };
        var day = calculator.AggregateDay(new DateOnly(2026, 8, 1), records);
        var period = calculator.AggregatePeriod(Period(), new[] { day }, Array.Empty<MonthlyAllowance>());

        Assert.Equal(100, day.CalculatedSubtotal.Value);
        Assert.Equal(1, day.UncalculatedCount);
        Assert.Equal(100, period.CalculatedSubtotal.Value);
        Assert.Equal(1, period.UncalculatedCount);
    }

    [Fact]
    public void AggregateDayRejectsDuplicateRecordsAndInconsistentDetails()
    {
        var record = Calculated(basePay: 100);
        Assert.Throws<ArgumentException>(() => calculator.AggregateDay(
            new DateOnly(2026, 8, 1),
            new[] { record, record }));

        var inconsistent = record with { Total = new YenAmount(101) };
        Assert.Throws<ArgumentException>(() => calculator.AggregateDay(
            new DateOnly(2026, 8, 1),
            new[] { inconsistent }));

        var guessed = Uncalculated() with { Total = new YenAmount(1) };
        Assert.Throws<ArgumentException>(() => calculator.AggregateDay(
            new DateOnly(2026, 8, 1),
            new[] { guessed }));
    }

    [Fact]
    public void AggregatePeriodRejectsDuplicateDaysAllowancesAndInconsistentDay()
    {
        var period = Period();
        var day = calculator.AggregateDay(new DateOnly(2026, 8, 1), new[] { Calculated(basePay: 100) });
        Assert.Throws<ArgumentException>(() => calculator.AggregatePeriod(
            period,
            new[] { day, day },
            Array.Empty<MonthlyAllowance>()));

        var allowance = Allowance(period.Key, 100);
        Assert.Throws<ArgumentException>(() => calculator.AggregatePeriod(
            period,
            Array.Empty<DailySalaryCalculation>(),
            new[] { allowance, allowance }));

        var inconsistentDay = day with { CalculatedSubtotal = new YenAmount(101) };
        Assert.Throws<ArgumentException>(() => calculator.AggregatePeriod(
            period,
            new[] { inconsistentDay },
            Array.Empty<MonthlyAllowance>()));
    }

    [Fact]
    public void AggregatesDefensivelyCopyTopLevelAndNestedCollections()
    {
        var mutablePremiums = new List<AppliedPremium>
        {
            AppliedPremium(10),
        };
        var mutableRecords = new List<WorkSalaryCalculation>
        {
            Calculated(premiums: new[] { 10L }, premiumDetails: mutablePremiums),
        };
        var day = calculator.AggregateDay(new DateOnly(2026, 8, 1), mutableRecords);
        mutablePremiums.Clear();
        mutableRecords.Clear();

        Assert.Single(day.Records);
        Assert.Single(day.Records[0].Premiums);

        var period = Period();
        var mutableDays = new List<DailySalaryCalculation> { day };
        var mutableAllowances = new List<MonthlyAllowance> { Allowance(period.Key, 20) };
        var result = calculator.AggregatePeriod(period, mutableDays, mutableAllowances);
        mutableDays.Clear();
        mutableAllowances.Clear();

        Assert.Single(result.Days);
        Assert.Single(result.Allowances);
        Assert.Throws<NotSupportedException>(() => ((IList<DailySalaryCalculation>)result.Days).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<MonthlyAllowance>)result.Allowances).Clear());
    }

    private static WorkSalaryCalculation Calculated(
        long basePay = 0,
        IReadOnlyList<long>? premiums = null,
        IReadOnlyList<long>? bonuses = null,
        IReadOnlyList<AppliedPremium>? premiumDetails = null)
    {
        var appliedPremiums = premiumDetails ??
            (premiums ?? Array.Empty<long>()).Select(AppliedPremium).ToArray();
        var appliedBonuses = (bonuses ?? Array.Empty<long>())
            .Select(amount => new AppliedCountBonus(
                new CountBonusId(Guid.NewGuid()),
                "件数",
                new YenAmount(amount)))
            .ToArray();
        var total = checked(basePay + appliedPremiums.Sum(item => item.Amount.Value) + appliedBonuses.Sum(item => item.Amount.Value));
        return new WorkSalaryCalculation(
            new WorkRecordId(Guid.NewGuid()),
            SalaryCalculationStatus.Calculated,
            TestData.Rate(RateType.FixedPerRecord, basePay),
            new YenAmount(basePay),
            appliedPremiums,
            appliedBonuses,
            new YenAmount(total),
            Array.Empty<MissingCalculationRequirement>());
    }

    private static AppliedPremium AppliedPremium(long amount) =>
        new(
            TestData.FixedPerRecordPremium(amount),
            new WorkMinutes(30),
            new YenAmount(amount));

    private static WorkSalaryCalculation Uncalculated() =>
        new(
            new WorkRecordId(Guid.NewGuid()),
            SalaryCalculationStatus.Uncalculated,
            null,
            null,
            Array.Empty<AppliedPremium>(),
            Array.Empty<AppliedCountBonus>(),
            null,
            new[] { new MissingCalculationRequirement(MissingCalculationRequirementCodes.Rate, TestData.ServiceId.Value) });

    private static PayrollPeriod Period() =>
        TestData.Period(2026, 8, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 20));

    private static MonthlyAllowance Allowance(PayrollPeriodKey key, long amount) =>
        new(new MonthlyAllowanceId(Guid.NewGuid()), key, "手当", new YenAmount(amount));
}
