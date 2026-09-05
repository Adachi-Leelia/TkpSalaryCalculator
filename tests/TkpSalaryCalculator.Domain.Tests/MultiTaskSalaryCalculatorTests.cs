using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Tests;

public sealed class MultiTaskSalaryCalculatorTests
{
    private static readonly ServiceId PhysicalServiceId =
        new(Guid.Parse("51000000-0000-0000-0000-000000000001"));
    private static readonly ServiceId LivingServiceId =
        new(Guid.Parse("51000000-0000-0000-0000-000000000002"));
    private static readonly WorkTaskId PhysicalTaskId =
        new(Guid.Parse("52000000-0000-0000-0000-000000000001"));
    private static readonly WorkTaskId LivingTaskId =
        new(Guid.Parse("52000000-0000-0000-0000-000000000002"));

    private readonly SalaryCalculator calculator = new();

    [Fact(DisplayName = "CALC-013 複数タスク給与と訪問単位の件数加算を合計する")]
    public void Calc013_MultipleTasksAndOneVisitBonus()
    {
        var result = Calculate(
            [Task(PhysicalTaskId, PhysicalServiceId, 0), Task(LivingTaskId, LivingServiceId, 1)],
            [Rate(PhysicalServiceId, 1000), Rate(LivingServiceId, 800)],
            [Bonus(150)]);

        Assert.Equal(SalaryCalculationStatus.Calculated, result.Status);
        Assert.Equal([1000L, 800L], result.TaskCalculations.Select(x => x.TaskSubtotal!.Value.Value));
        Assert.Equal(150, Assert.Single(result.CountBonuses).Amount.Value);
        Assert.Equal(1950, result.Total!.Value.Value);
    }

    [Fact(DisplayName = "CALC-014 同じ件数加算に一致するタスクが複数でも訪問へ1回だけ適用する")]
    public void Calc014_SameBonusMatchesMultipleTasksOnce()
    {
        var result = Calculate(
            [Task(PhysicalTaskId, PhysicalServiceId, 0), Task(LivingTaskId, PhysicalServiceId, 1)],
            [Rate(PhysicalServiceId, 1000)],
            [Bonus(150, new HashSet<ServiceId> { PhysicalServiceId })]);

        Assert.Single(result.CountBonuses);
        Assert.Equal(2150, result.Total!.Value.Value);
    }

    [Fact(DisplayName = "CALC-015 異なる件数加算ルールは訪問へ各1回適用する")]
    public void Calc015_DifferentBonusesEachApplyOnce()
    {
        var result = Calculate(
            [Task(PhysicalTaskId, PhysicalServiceId, 0), Task(LivingTaskId, LivingServiceId, 1)],
            [Rate(PhysicalServiceId, 1000), Rate(LivingServiceId, 800)],
            [
                Bonus(150, new HashSet<ServiceId> { PhysicalServiceId }),
                Bonus(75, new HashSet<ServiceId> { LivingServiceId }),
            ]);

        Assert.Equal([150L, 75L], result.CountBonuses.Select(x => x.Amount.Value));
        Assert.Equal(2025, result.Total!.Value.Value);
    }

    [Fact(DisplayName = "CALC-016 1タスクが単価不足なら訪問全体を未計算にする")]
    public void Calc016_OneMissingRateMakesVisitUncalculated()
    {
        var result = Calculate(
            [Task(PhysicalTaskId, PhysicalServiceId, 0), Task(LivingTaskId, LivingServiceId, 1)],
            [Rate(PhysicalServiceId, 1000)],
            [Bonus(150)]);

        Assert.Equal(SalaryCalculationStatus.Uncalculated, result.Status);
        Assert.Equal(SalaryCalculationStatus.Calculated, result.TaskCalculations[0].Status);
        Assert.Equal(SalaryCalculationStatus.Uncalculated, result.TaskCalculations[1].Status);
        Assert.All(result.MissingRequirements, requirement => Assert.Equal(LivingTaskId, requirement.WorkTaskId));
        Assert.Empty(result.CountBonuses);
        Assert.Null(result.Total);
    }

    [Fact(DisplayName = "CALC-017 タスク小計と件数加算は追加の丸めなしで整数加算する")]
    public void Calc017_TaskSubtotalsAreAddedWithoutAdditionalRounding()
    {
        var result = Calculate(
            [Task(PhysicalTaskId, PhysicalServiceId, 0, 30), Task(LivingTaskId, LivingServiceId, 1, 30)],
            [HourlyRate(PhysicalServiceId, 1001), HourlyRate(LivingServiceId, 1001)],
            [Bonus(1)]);

        Assert.Equal([501L, 501L], result.TaskCalculations.Select(x => x.TaskSubtotal!.Value.Value));
        Assert.Equal(1003, result.Total!.Value.Value);
    }

    [Fact(DisplayName = "CALC-018 タスク間の時刻の重複や空きは検証しない")]
    public void Calc018_TaskIntervalsAreIndependent()
    {
        var first = Task(PhysicalTaskId, PhysicalServiceId, 0, start: 9 * 60);
        var overlapping = Task(LivingTaskId, LivingServiceId, 1, start: 9 * 60 + 30);

        var result = Calculate(
            [first, overlapping],
            [Rate(PhysicalServiceId, 1000), Rate(LivingServiceId, 800)],
            []);

        Assert.Equal(SalaryCalculationStatus.Calculated, result.Status);
        Assert.Equal(1800, result.Total!.Value.Value);
    }

    [Fact(DisplayName = "CALC-019 未計算件数はタスク数ではなく訪問数で集計する")]
    public void Calc019_UncalculatedCountIsPerVisit()
    {
        var visit = Calculate(
            [Task(PhysicalTaskId, PhysicalServiceId, 0), Task(LivingTaskId, LivingServiceId, 1)],
            [],
            []);
        var day = calculator.AggregateDay(new DateOnly(2026, 8, 15), [visit]);
        var period = TestData.Period(2026, 8, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 20));
        var periodResult = calculator.AggregatePeriod(period, [day], []);
        var annual = new AnnualSalaryCalculator().Aggregate([periodResult]);

        Assert.Equal(2, visit.MissingRequirements.Select(x => x.WorkTaskId).Distinct().Count());
        Assert.Equal(1, day.UncalculatedCount);
        Assert.Equal(1, periodResult.UncalculatedCount);
        Assert.Equal(1, annual.UncalculatedCount);
    }

    [Fact(DisplayName = "CALC-038 複数タスクの訪問合計オーバーフローを検出する")]
    public void Calc038_VisitTotalOverflowIsRejected()
    {
        Assert.Throws<OverflowException>(() => Calculate(
            [Task(PhysicalTaskId, PhysicalServiceId, 0), Task(LivingTaskId, LivingServiceId, 1)],
            [Rate(PhysicalServiceId, long.MaxValue), Rate(LivingServiceId, 1)],
            []));
    }

    [Fact]
    public void WorkRecordRequiresUniqueContiguousTasksAndCopiesTheCollection()
    {
        var first = Task(PhysicalTaskId, PhysicalServiceId, 0);
        var second = Task(LivingTaskId, LivingServiceId, 1);
        var mutable = new List<WorkTask> { first, second };
        var record = new WorkRecord(new WorkRecordId(Guid.NewGuid()), new DateOnly(2026, 8, 15), mutable);

        mutable.Clear();

        Assert.Equal(2, record.Tasks.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<WorkTask>)record.Tasks).Clear());
        Assert.Throws<ArgumentException>(() => new WorkRecord(
            new WorkRecordId(Guid.NewGuid()), new DateOnly(2026, 8, 15), []));
        Assert.Throws<ArgumentException>(() => new WorkRecord(
            new WorkRecordId(Guid.NewGuid()), new DateOnly(2026, 8, 15), [first, first]));
        Assert.Throws<ArgumentException>(() => new WorkRecord(
            new WorkRecordId(Guid.NewGuid()), new DateOnly(2026, 8, 15),
            [first, Task(LivingTaskId, LivingServiceId, 2)]));
    }

    private WorkSalaryCalculation Calculate(
        IReadOnlyList<WorkTask> tasks,
        IReadOnlyList<SnapshotRate> rates,
        IReadOnlyList<SnapshotCountBonus> bonuses)
    {
        var snapshot = new SettingSnapshot(
            new SettingSnapshotId(Guid.NewGuid()),
            null,
            TestData.HolidayVersionId,
            new SchemaVersion(1),
            DateTimeOffset.Parse("2026-08-15T00:00:00Z"),
            [
                new SnapshotService(PhysicalServiceId, "身体1", new DisplayOrder(0), true),
                new SnapshotService(LivingServiceId, "生活3", new DisplayOrder(1), true),
            ],
            [],
            rates,
            [],
            bonuses);
        var record = new WorkRecord(
            new WorkRecordId(Guid.Parse("53000000-0000-0000-0000-000000000001")),
            new DateOnly(2026, 8, 15),
            tasks);
        return calculator.Calculate(TestData.Request(record, snapshot));
    }

    private static WorkTask Task(
        WorkTaskId id,
        ServiceId serviceId,
        int displayOrder,
        int minutes = 60,
        int? start = null) =>
        new(id, serviceId, null, WorkInputMode.Duration, new WorkMinutes(minutes),
            start is null ? null : new MinuteOfDay(start.Value), null, new DisplayOrder(displayOrder));

    private static SnapshotRate Rate(ServiceId serviceId, long amount) =>
        new(serviceId, null, RateType.FixedPerRecord, new YenAmount(amount));

    private static SnapshotRate HourlyRate(ServiceId serviceId, long amount) =>
        new(serviceId, null, RateType.Hourly, new YenAmount(amount));

    private static SnapshotCountBonus Bonus(long amount, IReadOnlySet<ServiceId>? services = null) =>
        new(new CountBonusId(Guid.NewGuid()), "件数加算", new YenAmount(amount),
            services ?? new HashSet<ServiceId>(), true);
}
