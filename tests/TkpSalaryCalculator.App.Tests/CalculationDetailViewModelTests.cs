using TkpSalaryCalculator.Domain.Services;

namespace TkpSalaryCalculator.App.Tests;

public sealed class CalculationDetailViewModelTests
{
    private static readonly PayrollPeriodKey PeriodKey = new(new YearMonth(2026, 8));
    private static readonly DateOnly WorkDate = new(2026, 8, 10);
    private static readonly WorkRecordId RecordId = new(Guid.Parse("40000000-0000-0000-0000-000000000001"));
    private static readonly WorkRecordId OtherRecordId = new(Guid.Parse("40000000-0000-0000-0000-000000000002"));
    private static readonly ServiceId ServiceId = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));
    private static readonly TimeCategoryId CategoryId = new(Guid.Parse("20000000-0000-0000-0000-000000000001"));

    [Fact]
    public async Task SCRCALC01_DisplaysPeriodDayAndWorkRecordBreakdownsFromSummaryDto()
    {
        var salary = new SalaryStub { Summary = Summary([CalculatedRecord(RecordId)]) };
        var viewModel = CreateViewModel(salary);
        viewModel.SetPayrollPeriod(PeriodKey);

        await viewModel.LoadAsync();

        Assert.Equal(PeriodKey, salary.RequestedKey);
        Assert.Equal("給与算定開始日: 2026年7月21日", viewModel.StartDateText);
        Assert.Equal("給与算定終了日: 2026年8月20日", viewModel.EndDateText);
        Assert.Equal("2,100円", viewModel.TotalText);
        Assert.Equal("給与期間合計", viewModel.TotalLabel);
        Assert.Equal("1,200円", viewModel.BasePayText);
        Assert.Equal("300円", viewModel.PremiumText);
        Assert.Equal("100円", viewModel.CountBonusText);
        Assert.Equal("500円", viewModel.AllowanceText);
        Assert.Equal("300円", Assert.Single(viewModel.PremiumTotals).AmountText);
        Assert.Equal("交通手当", Assert.Single(viewModel.Allowances).DisplayName);

        var day = Assert.Single(viewModel.Days);
        Assert.Equal("1,600円", day.TotalText);
        var record = Assert.Single(day.Records);
        Assert.Equal("訪問介護 / 60分", record.DisplayName);
        Assert.Equal("09:00～10:00 / 1時間", record.WorkTimeText);
        Assert.Equal("時給 1,200円", record.AppliedRateText);
        Assert.Equal("1,600円", record.TotalText);
        Assert.Equal("設定対象年月: 2026年8月", record.SettingMonthText);
        Assert.Equal("夜間割増", Assert.Single(record.Premiums).DisplayName);
        Assert.Equal("30分", Assert.Single(record.Premiums).ApplicableTimeText);
        Assert.Equal("訪問件数", Assert.Single(record.CountBonuses).DisplayName);
    }

    [Fact]
    public async Task SCRCALC01_RecordEntryShowsOnlyTheSelectedRecordWithoutPeriodTotals()
    {
        var selected = CalculatedRecord(RecordId);
        var uncalculated = UncalculatedRecord(OtherRecordId);
        var summary = Summary([selected, uncalculated]) with
        {
            Days =
            [
                new DailySalaryDto(WorkDate, [selected, uncalculated], new YenAmount(1_200),
                    new YenAmount(300), new YenAmount(100), new YenAmount(1_600), 1),
            ],
            UncalculatedCount = 1,
        };
        var salary = new SalaryStub { Summary = summary };
        var periods = new PayrollPeriodStub();
        var viewModel = CreateViewModel(salary, periods);
        viewModel.SetWorkRecord(WorkDate, RecordId);

        await viewModel.LoadAsync();

        Assert.Equal(WorkDate, periods.RequestedDate);
        Assert.False(viewModel.ShowsPayrollPeriodBreakdown);
        Assert.False(viewModel.HasPeriodUncalculated);
        Assert.False(viewModel.HasPremiumTotals);
        Assert.False(viewModel.HasAllowances);
        var day = Assert.Single(viewModel.Days);
        Assert.False(day.HasDaySubtotal);
        Assert.False(day.HasUncalculated);
        var record = Assert.Single(day.Records);
        Assert.Equal("訪問介護 / 60分", record.DisplayName);
    }

    [Fact]
    public async Task UI010_UncalculatedRecordShowsExactMissingSettingAndRepairDestination()
    {
        var work = Record(RecordId);
        var calculation = new WorkSalaryCalculation(
            RecordId, SalaryCalculationStatus.Uncalculated, null, null, [], [], null,
            [new MissingCalculationRequirement(MissingCalculationRequirementCodes.Rate, ServiceId.Value)]);
        var record = new WorkRecordSalaryDto(work, calculation, "訪問介護", "60分", new YearMonth(2026, 8));
        var summary = Summary([record]) with
        {
            Days =
            [
                new DailySalaryDto(WorkDate, [record], new YenAmount(0), new YenAmount(0), new YenAmount(0), new YenAmount(0), 1),
            ],
            BasePaySubtotal = new YenAmount(0),
            PremiumSubtotal = new YenAmount(0),
            CountBonusSubtotal = new YenAmount(0),
            CalculatedSubtotal = new YenAmount(500),
            UncalculatedCount = 1,
        };
        var salary = new SalaryStub { Summary = summary };
        var viewModel = CreateViewModel(salary);
        viewModel.SetPayrollPeriod(PeriodKey);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasUncalculated);
        Assert.Equal("計算済み分の小計", viewModel.TotalLabel);
        var row = Assert.Single(Assert.Single(viewModel.Days).Records);
        Assert.True(row.HasMissingReason);
        Assert.Contains("基本単価", row.MissingReasonText);
        Assert.Contains("サービス・単価", row.MissingReasonText);
        Assert.Equal("未計算", row.TotalText);
    }

    private static CalculationDetailViewModel CreateViewModel(
        SalaryStub salary,
        PayrollPeriodStub? periods = null) => new(
        salary,
        periods ?? new PayrollPeriodStub(),
        new JapaneseDisplayFormatter(),
        new UserErrorPresenter(),
        new AppSessionState(new DateOnly(2026, 8, 21)));

    private static PayrollPeriodSummaryDto Summary(IReadOnlyList<WorkRecordSalaryDto> records)
    {
        var day = new DailySalaryDto(
            WorkDate, records, new YenAmount(1_200), new YenAmount(300), new YenAmount(100), new YenAmount(1_600), 0);
        return new PayrollPeriodSummaryDto(
            new PayrollPeriod(PeriodKey, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 20)),
            [day],
            [new MonthlyAllowanceDto(new MonthlyAllowanceId(Guid.NewGuid()), "交通手当", new YenAmount(500))],
            new YenAmount(1_200), new YenAmount(300), new YenAmount(100), new YenAmount(500), new YenAmount(2_100), 0);
    }

    private static WorkRecordSalaryDto CalculatedRecord(WorkRecordId id)
    {
        var premium = new SnapshotPremium(
            new PremiumId(Guid.Parse("70000000-0000-0000-0000-000000000001")),
            "夜間割増", PremiumCalculationType.Percentage, new BasisPoints(2_500), null,
            new MinuteOfDay(1320), new MinuteOfDay(300), false,
            new HashSet<DayOfWeek>(), new HashSet<DateOnly>(), new HashSet<ServiceId>(), true);
        var calculation = new WorkSalaryCalculation(
            id,
            SalaryCalculationStatus.Calculated,
            new SnapshotRate(ServiceId, CategoryId, RateType.Hourly, new YenAmount(1_200)),
            new YenAmount(1_200),
            [new AppliedPremium(premium, new WorkMinutes(30), new YenAmount(300))],
            [new AppliedCountBonus(new CountBonusId(Guid.NewGuid()), "訪問件数", new YenAmount(100))],
            new YenAmount(1_600),
            []);
        return new WorkRecordSalaryDto(Record(id), calculation, "訪問介護", "60分", new YearMonth(2026, 8));
    }

    private static WorkRecordSalaryDto UncalculatedRecord(WorkRecordId id)
    {
        var calculation = new WorkSalaryCalculation(
            id, SalaryCalculationStatus.Uncalculated, null, null, [], [], null,
            [new MissingCalculationRequirement(MissingCalculationRequirementCodes.Rate, ServiceId.Value)]);
        return new WorkRecordSalaryDto(Record(id), calculation, "訪問介護", "60分", new YearMonth(2026, 8));
    }

    private static WorkRecordDto Record(WorkRecordId id) => new(
        id, WorkDate, ServiceId, CategoryId, WorkInputMode.TimeRange, new WorkMinutes(60),
        new MinuteOfDay(540), new MinuteOfDay(600), null, null, null);

    private sealed class SalaryStub : ISalaryQueryUseCase
    {
        public required PayrollPeriodSummaryDto Summary { get; init; }
        public PayrollPeriodKey? RequestedKey { get; private set; }

        public Task<CalendarMonthScreenDto> GetCalendarMonthScreenAsync(
            YearMonth yearMonth, DateOnly selectedDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<DayScreenDto> GetDayScreenAsync(DateOnly workDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PayrollPeriodSummaryDto> GetPayrollPeriodAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedKey = payrollPeriodKey;
            return Task.FromResult(Summary);
        }

        public Task<IReadOnlyList<CalendarDayDto>> GetCalendarMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<DailySalaryDto> GetDayAsync(DateOnly workDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class PayrollPeriodStub : IPayrollPeriodSettingsUseCase
    {
        public DateOnly? RequestedDate { get; private set; }

        public Task<PayrollPeriod> FindPeriodAsync(DateOnly localDate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedDate = localDate;
            return Task.FromResult(new PayrollPeriod(PeriodKey, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 20)));
        }

        public Task<ClosingRuleReplacementPreviewDto> PreviewClosingRuleReplacementAsync(ReplaceClosingRuleCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EffectiveClosingRuleDto?> GetClosingRuleAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceClosingRuleAsync(ReplaceClosingRuleCommand command, ClosingRuleReplacementConfirmationToken confirmationToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MonthlyAllowanceDto>> GetAllowancesAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonthlyAllowanceDto> SaveAllowanceAsync(SaveMonthlyAllowanceCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAllowanceAsync(MonthlyAllowanceId id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
