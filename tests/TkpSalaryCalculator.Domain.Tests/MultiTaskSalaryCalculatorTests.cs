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

    [Fact(DisplayName = "CALC-040 OFFは件数加算だけを除外し基本給与と割増を維持する")]
    public void Calc040_TogglePreservesTaskCalculations()
    {
        WorkTask[] tasks = [Task(PhysicalTaskId, PhysicalServiceId, 0), Task(LivingTaskId, LivingServiceId, 1)];
        SnapshotRate[] rates = [Rate(PhysicalServiceId, 900), Rate(LivingServiceId, 700)];
        SnapshotCountBonus[] bonuses = [Bonus(150)];
        SnapshotPremium[] premiums = [TestData.FixedPerRecordPremium(100)];

        var enabled = Calculate(tasks, rates, bonuses, premiums: premiums);
        var disabled = Calculate(tasks, rates, bonuses, false, premiums);

        Assert.True(enabled.IsCountBonusEnabled);
        Assert.False(disabled.IsCountBonusEnabled);
        Assert.Equal(1950, enabled.Total!.Value.Value);
        Assert.Equal(1800, disabled.Total!.Value.Value);
        Assert.Equal(1600, disabled.BasePay!.Value.Value);
        Assert.Empty(disabled.CountBonuses);
        Assert.Equal(SalaryCalculationStatus.Calculated, disabled.Status);
        Assert.Equal([1000L, 800L], disabled.TaskCalculations.Select(task => task.TaskSubtotal!.Value.Value));
        for (var index = 0; index < tasks.Length; index++)
        {
            var on = enabled.TaskCalculations[index];
            var off = disabled.TaskCalculations[index];
            Assert.Equal(on.AppliedRate, off.AppliedRate);
            Assert.Equal(on.BasePay, off.BasePay);
            Assert.Equal(on.Premiums.ToArray(), off.Premiums.ToArray());
            Assert.Equal(on.TaskSubtotal, off.TaskSubtotal);
        }
    }

    [Theory(DisplayName = "CALC-041 同じルールに複数タスクが一致してもONは1回、OFFは0回")]
    [InlineData(true, 2150, 1)]
    [InlineData(false, 2000, 0)]
    public void Calc041_ToggleControlsSharedRule(bool enabled, long total, int bonusCount)
    {
        var result = Calculate(
            [Task(PhysicalTaskId, PhysicalServiceId, 0), Task(LivingTaskId, PhysicalServiceId, 1)],
            [Rate(PhysicalServiceId, 1000)],
            [Bonus(150, new HashSet<ServiceId> { PhysicalServiceId })], enabled);

        Assert.Equal(total, result.Total!.Value.Value);
        Assert.Equal(bonusCount, result.CountBonuses.Count);
    }

    [Theory(DisplayName = "CALC-042 OFFは異なる件数加算ルールをすべて除外する")]
    [InlineData(true, 2050, 250)]
    [InlineData(false, 1800, 0)]
    public void Calc042_ToggleControlsAllRules(bool enabled, long total, long bonusSubtotal)
    {
        var result = Calculate(
            [Task(PhysicalTaskId, PhysicalServiceId, 0), Task(LivingTaskId, LivingServiceId, 1)],
            [Rate(PhysicalServiceId, 1000), Rate(LivingServiceId, 800)],
            [Bonus(150, new HashSet<ServiceId> { PhysicalServiceId }), Bonus(100, new HashSet<ServiceId> { LivingServiceId })],
            enabled);

        Assert.Equal(total, result.Total!.Value.Value);
        Assert.Equal(bonusSubtotal, result.CountBonuses.Sum(bonus => bonus.Amount.Value));
        if (!enabled) Assert.Empty(result.CountBonuses);
    }

    [Theory(DisplayName = "CALC-043 適用ルールがなくても選択値を計算結果に保持する")]
    [InlineData(true, "none")]
    [InlineData(false, "none")]
    [InlineData(true, "disabled")]
    [InlineData(false, "disabled")]
    [InlineData(true, "unmatched")]
    [InlineData(false, "unmatched")]
    public void Calc043_NoApplicableBonusPreservesSelection(bool enabled, string ruleCase)
    {
        SnapshotCountBonus[] bonuses = ruleCase switch
        {
            "disabled" => [new SnapshotCountBonus(new CountBonusId(Guid.NewGuid()), "無効", new YenAmount(150), new HashSet<ServiceId>(), false)],
            "unmatched" => [Bonus(150, new HashSet<ServiceId> { LivingServiceId })],
            _ => [],
        };
        var result = Calculate([Task(PhysicalTaskId, PhysicalServiceId, 0)], [Rate(PhysicalServiceId, 1000)], bonuses, enabled);

        Assert.Equal(enabled, result.IsCountBonusEnabled);
        Assert.Empty(result.CountBonuses);
        Assert.Equal(1000, result.Total!.Value.Value);
    }

    [Theory(DisplayName = "CALC-044 OFFでも単価不足の訪問を未計算のまま保持する")]
    [InlineData(true)]
    [InlineData(false)]
    public void Calc044_ToggleDoesNotHideMissingRate(bool enabled)
    {
        var result = Calculate(
            [Task(PhysicalTaskId, PhysicalServiceId, 0), Task(LivingTaskId, LivingServiceId, 1)],
            [Rate(PhysicalServiceId, 1000)], [Bonus(150)], enabled);

        Assert.Equal(enabled, result.IsCountBonusEnabled);
        Assert.Equal(SalaryCalculationStatus.Uncalculated, result.Status);
        Assert.Equal(SalaryCalculationStatus.Calculated, result.TaskCalculations[0].Status);
        Assert.Equal(LivingTaskId, Assert.Single(result.MissingRequirements).WorkTaskId);
        Assert.Empty(result.CountBonuses);
        Assert.Null(result.Total);
        Assert.Null(result.BasePay);
        var day = calculator.AggregateDay(new DateOnly(2026, 8, 15), [result]);
        Assert.Equal(enabled, Assert.Single(day.Records).IsCountBonusEnabled);
        Assert.Equal(1, day.UncalculatedCount);
    }

    [Fact]
    public void MixedSelectionsPreserveDailyPeriodAndAnnualTotals()
    {
        WorkTask[] tasks = [Task(PhysicalTaskId, PhysicalServiceId, 0), Task(LivingTaskId, LivingServiceId, 1)];
        SnapshotRate[] rates = [Rate(PhysicalServiceId, 1000), Rate(LivingServiceId, 800)];
        SnapshotCountBonus[] bonuses = [Bonus(150)];
        var enabled = Calculate(tasks, rates, bonuses);
        var disabled = Calculate(tasks, rates, bonuses, false) with { WorkRecordId = new WorkRecordId(Guid.NewGuid()) };
        var day = calculator.AggregateDay(new DateOnly(2026, 8, 15), [enabled, disabled]);
        var period = TestData.Period(2026, 8, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 20));
        var periodResult = calculator.AggregatePeriod(period, [day],
            [new MonthlyAllowance(new MonthlyAllowanceId(Guid.NewGuid()), period.Key, "月額手当", new YenAmount(5000))]);
        var annual = new AnnualSalaryCalculator().Aggregate([periodResult]);

        Assert.Equal(2, day.Records.Count);
        Assert.Equal([true, false], day.Records.Select(record => record.IsCountBonusEnabled));
        Assert.Equal(3750, day.CalculatedSubtotal.Value);
        Assert.Equal(150, day.CountBonusSubtotal.Value);
        Assert.Equal(8750, periodResult.CalculatedSubtotal.Value);
        Assert.False(periodResult.Days[0].Records[1].IsCountBonusEnabled);
        Assert.Equal(8750, annual.CalculatedSubtotal.Value);
        Assert.Throws<ArgumentException>(() => calculator.AggregateDay(day.WorkDate,
            [enabled with { IsCountBonusEnabled = false }]));
    }

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

        Assert.True(record.IsCountBonusEnabled);
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
        IReadOnlyList<SnapshotCountBonus> bonuses,
        bool isCountBonusEnabled = true,
        IReadOnlyList<SnapshotPremium>? premiums = null)
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
            premiums ?? [],
            bonuses);
        var record = new WorkRecord(
            new WorkRecordId(Guid.Parse("53000000-0000-0000-0000-000000000001")),
            new DateOnly(2026, 8, 15),
            tasks,
            isCountBonusEnabled);
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
