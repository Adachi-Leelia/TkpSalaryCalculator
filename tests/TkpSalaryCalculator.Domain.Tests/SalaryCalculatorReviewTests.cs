using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Tests;

public sealed class SalaryCalculatorReviewTests
{
    private readonly SalaryCalculator calculator = new();

    [Fact]
    public void MissingServiceReturnsExplicitUncalculatedReason()
    {
        var missingService = new ServiceId(Guid.NewGuid());
        var result = Calculate(
            Work(serviceId: missingService),
            Snapshot(rates: [TestData.Rate(RateType.Hourly, 1200)]));

        Assert.Equal(SalaryCalculationStatus.Uncalculated, result.Status);
        Assert.Contains(result.MissingRequirements,
            reason => reason.Code == MissingCalculationRequirementCodes.Service && reason.RelatedId == missingService.Value);
    }

    [Fact]
    public void MissingTimeCategoryReturnsExplicitUncalculatedReason()
    {
        var missingCategory = new TimeCategoryId(Guid.NewGuid());
        var result = Calculate(
            Work(categoryId: missingCategory),
            Snapshot(rates: [TestData.Rate(RateType.Hourly, 1200)]));

        Assert.Equal(SalaryCalculationStatus.Uncalculated, result.Status);
        Assert.Contains(result.MissingRequirements,
            reason => reason.Code == MissingCalculationRequirementCodes.TimeCategory && reason.RelatedId == missingCategory.Value);
    }

    [Fact]
    public void ExistingCategoryFallsBackToServiceRateWhenCategoryRateIsAbsent()
    {
        var result = Calculate(
            Work(categoryId: TestData.CategoryId, minutes: 30),
            Snapshot(
                categories: [Category(TestData.CategoryId, TestData.ServiceId, true)],
                rates: [TestData.Rate(RateType.Hourly, 1200)]));

        Assert.Null(result.AppliedRate!.TimeCategoryId);
        Assert.Equal(600, result.BasePay!.Value.Value);
    }

    [Fact]
    public void DisabledServiceAndCategoryRemainValidForExistingWork()
    {
        var result = Calculate(
            Work(categoryId: TestData.CategoryId),
            Snapshot(
                services: [Service(TestData.ServiceId, false)],
                categories: [Category(TestData.CategoryId, TestData.ServiceId, false)],
                rates: [TestData.CategoryRate(RateType.FixedPerRecord, 850)]));

        Assert.Equal(SalaryCalculationStatus.Calculated, result.Status);
        Assert.Equal(850, result.Total!.Value.Value);
    }

    [Fact]
    public void DisabledPremiumAndCountBonusAreExcluded()
    {
        var result = Calculate(
            Work(),
            Snapshot(
                rates: [TestData.Rate(RateType.FixedPerRecord, 100)],
                premiums: [Premium(PremiumCalculationType.FixedPerRecord, amount: 200, enabled: false)],
                bonuses: [Bonus(300, enabled: false)]));

        Assert.Empty(result.Premiums);
        Assert.Empty(result.CountBonuses);
        Assert.Equal(100, result.Total!.Value.Value);
    }

    [Fact]
    public void ServiceTargetIsAndedWithDateAndTimeConditions()
    {
        var secondService = new ServiceId(Guid.NewGuid());
        var result = Calculate(
            Work(start: 22 * 60, date: new DateOnly(2026, 8, 15)),
            Snapshot(
                services: [Service(TestData.ServiceId, true), Service(secondService, true)],
                rates: [TestData.Rate(RateType.FixedPerRecord, 100)],
                premiums:
                [
                    Premium(
                        PremiumCalculationType.FixedPerRecord,
                        amount: 200,
                        start: 22 * 60,
                        end: 5 * 60,
                        weekdays: [DayOfWeek.Saturday],
                        serviceIds: [secondService]),
                ],
                bonuses: [Bonus(300, serviceIds: [secondService])]));

        Assert.Empty(result.Premiums);
        Assert.Empty(result.CountBonuses);
        Assert.Equal(100, result.Total!.Value.Value);
    }

    [Fact]
    public void WeekdayDateConditionApplies()
    {
        var result = Calculate(
            Work(date: new DateOnly(2026, 8, 15)),
            Snapshot(
                rates: [TestData.Rate(RateType.FixedPerRecord, 0)],
                premiums:
                [
                    Premium(
                        PremiumCalculationType.FixedPerRecord,
                        amount: 100,
                        weekdays: [DayOfWeek.Saturday]),
                ]));

        Assert.Equal(100, Assert.Single(result.Premiums).Amount.Value);
    }

    [Fact]
    public void NationalHolidayDateConditionApplies()
    {
        var date = new DateOnly(2026, 8, 17);
        var result = Calculate(
            Work(date: date),
            Snapshot(
                rates: [TestData.Rate(RateType.FixedPerRecord, 0)],
                premiums:
                [
                    Premium(
                        PremiumCalculationType.FixedPerRecord,
                        amount: 100,
                        usesNationalHolidays: true),
                ]),
            holidays: [date]);

        Assert.Equal(100, Assert.Single(result.Premiums).Amount.Value);
    }

    [Fact]
    public void IndividualDateConditionApplies()
    {
        var date = new DateOnly(2026, 8, 18);
        var result = Calculate(
            Work(date: date),
            Snapshot(
                rates: [TestData.Rate(RateType.FixedPerRecord, 0)],
                premiums:
                [
                    Premium(
                        PremiumCalculationType.FixedPerRecord,
                        amount: 100,
                        dates: [date]),
                ]));

        Assert.Equal(100, Assert.Single(result.Premiums).Amount.Value);
    }

    [Fact]
    public void RuleWithoutDateConditionAppliesEveryDay()
    {
        var result = Calculate(
            Work(date: new DateOnly(2026, 8, 19)),
            Snapshot(
                rates: [TestData.Rate(RateType.FixedPerRecord, 0)],
                premiums: [Premium(PremiumCalculationType.FixedPerRecord, amount: 100)]));

        Assert.Equal(100, Assert.Single(result.Premiums).Amount.Value);
    }

    [Fact]
    public void HalfOpenTimeWindowDoesNotIncludeWorkEndingAtWindowStart()
    {
        var result = Calculate(
            Work(minutes: 60, start: 21 * 60),
            Snapshot(
                rates: [TestData.Rate(RateType.FixedPerRecord, 100)],
                premiums:
                [
                    Premium(
                        PremiumCalculationType.FixedPerHour,
                        amount: 60,
                        start: 22 * 60,
                        end: 5 * 60),
                ]));

        Assert.Empty(result.Premiums);
        Assert.Equal(100, result.Total!.Value.Value);
    }

    [Fact]
    public void TwentyFourHourWorkDoesNotDoubleCountCrossMidnightWindow()
    {
        var result = Calculate(
            Work(minutes: 1440, start: 0),
            Snapshot(
                rates: [TestData.Rate(RateType.FixedPerRecord, 0)],
                premiums:
                [
                    Premium(
                        PremiumCalculationType.FixedPerHour,
                        amount: 60,
                        start: 22 * 60,
                        end: 5 * 60),
                ]));

        var premium = Assert.Single(result.Premiums);
        Assert.Equal(7 * 60, premium.ApplicableMinutes.Value);
        Assert.Equal(420, premium.Amount.Value);
    }

    [Fact]
    public void MultipleCountBonusesAllApplyOnce()
    {
        var result = Calculate(
            Work(),
            Snapshot(
                rates: [TestData.Rate(RateType.FixedPerRecord, 0)],
                bonuses: [Bonus(100), Bonus(200)]));

        Assert.Equal([100, 200], result.CountBonuses.Select(item => item.Amount.Value));
        Assert.Equal(300, result.Total!.Value.Value);
    }

    [Fact]
    public void ZeroMoneyAndZeroPercentageRemainValidIntegerDetails()
    {
        var result = Calculate(
            Work(),
            Snapshot(
                rates: [TestData.Rate(RateType.FixedPerRecord, 0)],
                premiums:
                [
                    Premium(PremiumCalculationType.Percentage, percentage: 0),
                    Premium(PremiumCalculationType.FixedPerRecord, amount: 0),
                ],
                bonuses: [Bonus(0)]));

        Assert.Equal(2, result.Premiums.Count);
        Assert.All(result.Premiums, item => Assert.Equal(0, item.Amount.Value));
        Assert.Equal(0, Assert.Single(result.CountBonuses).Amount.Value);
        Assert.Equal(0, result.Total!.Value.Value);
    }

    [Fact]
    public void PercentageMultiplicationOverflowIsDetected()
    {
        Assert.Throws<OverflowException>(() => Calculate(
            Work(),
            Snapshot(
                rates: [TestData.Rate(RateType.FixedPerRecord, long.MaxValue)],
                premiums: [Premium(PremiumCalculationType.Percentage, percentage: int.MaxValue)])));
    }

    [Fact]
    public void FixedPerHourMultiplicationOverflowIsDetected()
    {
        Assert.Throws<OverflowException>(() => Calculate(
            Work(minutes: 1440),
            Snapshot(
                rates: [TestData.Rate(RateType.FixedPerRecord, 0)],
                premiums: [Premium(PremiumCalculationType.FixedPerHour, amount: long.MaxValue)])));
    }

    [Fact]
    public void LongMaxValueFixedPayWithoutAdditionsSucceeds()
    {
        var result = Calculate(
            Work(),
            Snapshot(rates: [TestData.Rate(RateType.FixedPerRecord, long.MaxValue)]));

        Assert.Equal(long.MaxValue, result.BasePay!.Value.Value);
        Assert.Equal(long.MaxValue, result.Total!.Value.Value);
    }

    private WorkSalaryCalculation Calculate(
        WorkRecord workRecord,
        SettingSnapshot snapshot,
        IEnumerable<DateOnly>? holidays = null)
    {
        return calculator.Calculate(TestData.Request(workRecord, snapshot, holidays));
    }


    private static WorkRecord Work(
        int minutes = 30,
        DateOnly? date = null,
        int? start = null,
        ServiceId? serviceId = null,
        TimeCategoryId? categoryId = null)
    {
        return new(
            new WorkRecordId(Guid.NewGuid()),
            date ?? new DateOnly(2026, 8, 15),
            serviceId ?? TestData.ServiceId,
            categoryId,
            WorkInputMode.Duration,
            new WorkMinutes(minutes),
            start is null ? null : new MinuteOfDay(start.Value),
            null);
    }


    private static SettingSnapshot Snapshot(
        IReadOnlyList<SnapshotService>? services = null,
        IReadOnlyList<SnapshotTimeCategory>? categories = null,
        IReadOnlyList<SnapshotRate>? rates = null,
        IReadOnlyList<SnapshotPremium>? premiums = null,
        IReadOnlyList<SnapshotCountBonus>? bonuses = null)
    {
        return new(
            new SettingSnapshotId(Guid.NewGuid()),
            null,
            TestData.HolidayVersionId,
            new SchemaVersion(1),
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            services ?? [Service(TestData.ServiceId, true)],
            categories ?? [],
            rates ?? [],
            premiums ?? [],
            bonuses ?? []);
    }


    private static SnapshotService Service(ServiceId id, bool enabled)
    {
        return new(id, id == TestData.ServiceId ? "身体" : "生活", new DisplayOrder(0), enabled);
    }


    private static SnapshotTimeCategory Category(TimeCategoryId id, ServiceId serviceId, bool enabled)
    {
        return new(id, serviceId, "30分", new WorkMinutes(30), new DisplayOrder(0), enabled);
    }


    private static SnapshotPremium Premium(
        PremiumCalculationType type,
        int? percentage = null,
        long? amount = null,
        int? start = null,
        int? end = null,
        bool usesNationalHolidays = false,
        IEnumerable<DayOfWeek>? weekdays = null,
        IEnumerable<DateOnly>? dates = null,
        IEnumerable<ServiceId>? serviceIds = null,
        bool enabled = true)
    {
        return new(
            new PremiumId(Guid.NewGuid()),
            "割増",
            type,
            percentage is null ? null : new BasisPoints(percentage.Value),
            amount is null ? null : new YenAmount(amount.Value),
            start is null ? null : new MinuteOfDay(start.Value),
            end is null ? null : new MinuteOfDay(end.Value),
            usesNationalHolidays,
            new HashSet<DayOfWeek>(weekdays ?? []),
            new HashSet<DateOnly>(dates ?? []),
            new HashSet<ServiceId>(serviceIds ?? []),
            enabled);
    }


    private static SnapshotCountBonus Bonus(
        long amount,
        IEnumerable<ServiceId>? serviceIds = null,
        bool enabled = true)
    {
        return new(
            new CountBonusId(Guid.NewGuid()),
            "件数",
            new YenAmount(amount),
            new HashSet<ServiceId>(serviceIds ?? []),
            enabled);
    }

}
