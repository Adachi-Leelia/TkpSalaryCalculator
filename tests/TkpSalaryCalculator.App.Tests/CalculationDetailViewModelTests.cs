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

        Assert.Collection(
            viewModel.Rows,
            row => Assert.IsType<CalculationSectionHeaderRowViewModel>(row),
            row => Assert.IsType<CalculationPremiumTotalRowViewModel>(row),
            row => Assert.IsType<CalculationSectionHeaderRowViewModel>(row),
            row => Assert.IsType<CalculationAllowanceRowViewModel>(row),
            row => Assert.IsType<CalculationDayRowViewModel>(row),
            row => Assert.IsType<CalculationVisitRowViewModel>(row),
            row => Assert.IsType<CalculationWorkRecordRowViewModel>(row),
            row => Assert.IsType<CalculationPremiumRowViewModel>(row),
            row => Assert.IsType<CalculationCountBonusRowViewModel>(row),
            row => Assert.IsType<CalculationWorkRecordTotalRowViewModel>(row));
        var day = Assert.Single(viewModel.Rows.OfType<CalculationDayRowViewModel>());
        Assert.Equal("1,600円", day.TotalText);
        var record = Assert.Single(viewModel.Rows.OfType<CalculationWorkRecordRowViewModel>());
        Assert.Equal("訪問介護 / 60分", record.DisplayName);
        Assert.Equal("09:00～10:00 / 1時間", record.WorkTimeText);
        Assert.Equal("時給 1,200円", record.AppliedRateText);
        var total = Assert.Single(viewModel.Rows.OfType<CalculationWorkRecordTotalRowViewModel>());
        Assert.Equal("1,600円", total.TotalText);
        Assert.Equal("設定対象年月: 2026年8月", total.SettingMonthText);
        Assert.Equal("夜間割増", Assert.Single(viewModel.Rows.OfType<CalculationPremiumRowViewModel>()).DisplayName);
        Assert.Equal("30分", Assert.Single(viewModel.Rows.OfType<CalculationPremiumRowViewModel>()).ApplicableTimeText);
        Assert.Equal("訪問件数", Assert.Single(viewModel.Rows.OfType<CalculationCountBonusRowViewModel>()).DisplayName);
    }

    [Fact]
    public async Task UI020_MultiTaskVisitShowsVisitTasksCountBonusAndVisitTotalInHierarchy()
    {
        var secondServiceId = new ServiceId(Guid.Parse("10000000-0000-0000-0000-000000000002"));
        var firstTaskId = new WorkTaskId(Guid.Parse("41000000-0000-0000-0000-000000000001"));
        var secondTaskId = new WorkTaskId(Guid.Parse("41000000-0000-0000-0000-000000000002"));
        var firstTask = new WorkTaskDto(firstTaskId, ServiceId, null, WorkInputMode.Duration,
            new WorkMinutes(30), null, null, new DisplayOrder(0), null);
        var secondTask = new WorkTaskDto(secondTaskId, secondServiceId, null, WorkInputMode.Duration,
            new WorkMinutes(45), null, null, new DisplayOrder(1), null);
        var firstCalculation = new TaskSalaryCalculation(firstTaskId, SalaryCalculationStatus.Calculated,
            new SnapshotRate(ServiceId, null, RateType.FixedPerRecord, new YenAmount(1_000)),
            new YenAmount(1_000), [], new YenAmount(1_000), []);
        var secondCalculation = new TaskSalaryCalculation(secondTaskId, SalaryCalculationStatus.Calculated,
            new SnapshotRate(secondServiceId, null, RateType.FixedPerRecord, new YenAmount(800)),
            new YenAmount(800), [], new YenAmount(800), []);
        var visitCalculation = new WorkSalaryCalculation(
            RecordId, SalaryCalculationStatus.Calculated, [firstCalculation, secondCalculation],
            [new AppliedCountBonus(new CountBonusId(Guid.NewGuid()), "訪問件数", new YenAmount(150))],
            new YenAmount(1_950), []);
        var visit = new WorkRecordDto(RecordId, WorkDate, [firstTask, secondTask], null, null);
        var salaryRecord = new WorkRecordSalaryDto(visit, visitCalculation, new YearMonth(2026, 8),
        [
            new WorkTaskSalaryDto(firstTask, firstCalculation, "身体1", null),
            new WorkTaskSalaryDto(secondTask, secondCalculation, "生活3", null),
        ]);
        var summary = Summary([salaryRecord]) with
        {
            Days = [new DailySalaryDto(WorkDate, [salaryRecord], new YenAmount(1_800), new YenAmount(0),
                new YenAmount(150), new YenAmount(1_950), 0)],
            BasePaySubtotal = new YenAmount(1_800),
            PremiumSubtotal = new YenAmount(0),
            CountBonusSubtotal = new YenAmount(150),
            CalculatedSubtotal = new YenAmount(2_450),
        };
        var viewModel = CreateViewModel(new SalaryStub { Summary = summary });
        viewModel.SetWorkRecord(WorkDate, RecordId);

        await viewModel.LoadAsync();

        var visitRow = Assert.Single(viewModel.Rows.OfType<CalculationVisitRowViewModel>());
        Assert.Equal("タスク 2件", visitRow.TaskCountText);
        Assert.Equal("身体1、生活3", visitRow.TaskSummaryText);
        Assert.Equal(["タスク 1", "タスク 2"],
            viewModel.Rows.OfType<CalculationWorkRecordRowViewModel>().Select(row => row.TaskTitle));
        Assert.Equal(["1,000円", "800円"],
            viewModel.Rows.OfType<CalculationWorkRecordRowViewModel>().Select(row => row.TaskSubtotalText));
        Assert.Equal("150円", Assert.Single(viewModel.Rows.OfType<CalculationCountBonusRowViewModel>()).AmountText);
        var total = Assert.Single(viewModel.Rows.OfType<CalculationWorkRecordTotalRowViewModel>());
        Assert.True(total.HasTotal);
        Assert.Equal("1,950円", total.TotalText);
    }

    [Fact]
    public async Task RSP004_RecordEntryUsesSingleRecordQueryWithoutPeriodTotals()
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
        var viewModel = CreateViewModel(salary);
        viewModel.SetWorkRecord(WorkDate, RecordId);

        await viewModel.LoadAsync();

        Assert.Equal(RecordId, salary.RequestedWorkRecordId);
        Assert.Null(salary.RequestedKey);
        Assert.False(viewModel.ShowsPayrollPeriodBreakdown);
        Assert.False(viewModel.HasPeriodUncalculated);
        Assert.False(viewModel.HasPremiumTotals);
        Assert.False(viewModel.HasAllowances);
        var day = Assert.Single(viewModel.Rows.OfType<CalculationDayRowViewModel>());
        Assert.False(day.HasDaySubtotal);
        Assert.False(day.HasUncalculated);
        var record = Assert.Single(viewModel.Rows.OfType<CalculationWorkRecordRowViewModel>());
        Assert.Equal("訪問介護 / 60分", record.DisplayName);
    }

    [Fact]
    public async Task UI010_UncalculatedRecordShowsExactMissingSettingAndRepairDestination()
    {
        var work = Record(RecordId);
        var task = Assert.Single(work.Tasks);
        var missing = new MissingCalculationRequirement(
            task.Id, MissingCalculationRequirementCodes.Rate, ServiceId.Value);
        var calculation = new WorkSalaryCalculation(
            RecordId, SalaryCalculationStatus.Uncalculated,
            [new TaskSalaryCalculation(task.Id, SalaryCalculationStatus.Uncalculated,
                null, null, [], null, [missing])], [], null, [missing]);
        var record = SalaryRecord(work, calculation);
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
        var row = Assert.Single(viewModel.Rows.OfType<CalculationWorkRecordTotalRowViewModel>());
        Assert.True(row.HasMissingReason);
        Assert.Contains("基本単価", row.MissingReasonText);
        Assert.Contains("サービス・単価", row.MissingReasonText);
        Assert.Equal("未計算", row.TotalText);
        Assert.False(row.HasTotal);
        Assert.Empty(viewModel.Rows.OfType<CalculationCountBonusRowViewModel>());
    }

    [Fact]
    public async Task UI021_DiagnosticPremiumFromUncalculatedVisitIsNotIncludedInPeriodPremiumTotals()
    {
        var firstTaskId = new WorkTaskId(Guid.Parse("41000000-0000-0000-0000-000000000011"));
        var secondTaskId = new WorkTaskId(Guid.Parse("41000000-0000-0000-0000-000000000012"));
        var secondServiceId = new ServiceId(Guid.Parse("10000000-0000-0000-0000-000000000012"));
        var premiumRule = new SnapshotPremium(
            new PremiumId(Guid.Parse("70000000-0000-0000-0000-000000000011")),
            "夜間割増", PremiumCalculationType.FixedPerRecord, null, new YenAmount(300),
            null, null, false, new HashSet<DayOfWeek>(), new HashSet<DateOnly>(),
            new HashSet<ServiceId>(), true);
        var firstTask = new WorkTaskDto(firstTaskId, ServiceId, null, WorkInputMode.Duration,
            new WorkMinutes(30), null, null, new DisplayOrder(0), null);
        var secondTask = new WorkTaskDto(secondTaskId, secondServiceId, null, WorkInputMode.Duration,
            new WorkMinutes(45), null, null, new DisplayOrder(1), null);
        var firstCalculation = new TaskSalaryCalculation(
            firstTaskId, SalaryCalculationStatus.Calculated,
            new SnapshotRate(ServiceId, null, RateType.FixedPerRecord, new YenAmount(1_000)),
            new YenAmount(1_000), [new AppliedPremium(premiumRule, new WorkMinutes(30), new YenAmount(300))],
            new YenAmount(1_300), []);
        var missing = new MissingCalculationRequirement(
            secondTaskId, MissingCalculationRequirementCodes.Rate, secondServiceId.Value);
        var secondCalculation = new TaskSalaryCalculation(
            secondTaskId, SalaryCalculationStatus.Uncalculated, null, null, [], null, [missing]);
        var calculation = new WorkSalaryCalculation(
            RecordId, SalaryCalculationStatus.Uncalculated, [firstCalculation, secondCalculation], [], null, [missing]);
        var work = new WorkRecordDto(RecordId, WorkDate, [firstTask, secondTask], null, null);
        var record = new WorkRecordSalaryDto(work, calculation, new YearMonth(2026, 8),
        [
            new WorkTaskSalaryDto(firstTask, firstCalculation, "身体1", null),
            new WorkTaskSalaryDto(secondTask, secondCalculation, "生活3", null),
        ]);
        var summary = Summary([record]) with
        {
            Days = [new DailySalaryDto(WorkDate, [record], new YenAmount(0), new YenAmount(0),
                new YenAmount(0), new YenAmount(0), 1)],
            BasePaySubtotal = new YenAmount(0),
            PremiumSubtotal = new YenAmount(0),
            CountBonusSubtotal = new YenAmount(0),
            CalculatedSubtotal = new YenAmount(500),
            UncalculatedCount = 1,
        };
        var viewModel = CreateViewModel(new SalaryStub { Summary = summary });
        viewModel.SetPayrollPeriod(PeriodKey);

        await viewModel.LoadAsync();

        Assert.Empty(viewModel.PremiumTotals);
        Assert.Empty(viewModel.Rows.OfType<CalculationPremiumTotalRowViewModel>());
        Assert.Single(viewModel.Rows.OfType<CalculationPremiumRowViewModel>());
    }

    [Fact]
    public async Task RSP003_DisplayRowTransformationRunsOutsideCapturedUiContext()
    {
        var context = new TrackingSynchronizationContext();
        var days = new ContextTrackingReadOnlyList<DailySalaryDto>(
            Summary([CalculatedRecord(RecordId)]).Days,
            context);
        var salary = new SalaryStub
        {
            Summary = Summary([CalculatedRecord(RecordId)]) with { Days = days },
        };
        var viewModel = CreateViewModel(salary);
        viewModel.SetPayrollPeriod(PeriodKey);
        var rowsPublishedOnUiContext = false;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CalculationDetailViewModel.Rows))
                rowsPublishedOnUiContext = ReferenceEquals(SynchronizationContext.Current, context);
        };

        await Task.Run(async () =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                await viewModel.LoadAsync();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        });

        Assert.True(days.AccessCount > 0);
        Assert.Equal(0, days.UiContextAccessCount);
        Assert.True(rowsPublishedOnUiContext);
        Assert.NotEmpty(viewModel.Rows);
    }

    private static CalculationDetailViewModel CreateViewModel(SalaryStub salary) => new(
        salary,
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
        var work = Record(id);
        var task = Assert.Single(work.Tasks);
        var taskCalculation = new TaskSalaryCalculation(
            task.Id, SalaryCalculationStatus.Calculated,
            new SnapshotRate(ServiceId, CategoryId, RateType.Hourly, new YenAmount(1_200)),
            new YenAmount(1_200),
            [new AppliedPremium(premium, new WorkMinutes(30), new YenAmount(300))],
            new YenAmount(1_500), []);
        var calculation = new WorkSalaryCalculation(
            id, SalaryCalculationStatus.Calculated, [taskCalculation],
            [new AppliedCountBonus(new CountBonusId(Guid.NewGuid()), "訪問件数", new YenAmount(100))],
            new YenAmount(1_600), []);
        return SalaryRecord(work, calculation);
    }

    private static WorkRecordSalaryDto UncalculatedRecord(WorkRecordId id)
    {
        var work = Record(id);
        var task = Assert.Single(work.Tasks);
        var missing = new MissingCalculationRequirement(
            task.Id, MissingCalculationRequirementCodes.Rate, ServiceId.Value);
        var calculation = new WorkSalaryCalculation(
            id, SalaryCalculationStatus.Uncalculated,
            [new TaskSalaryCalculation(task.Id, SalaryCalculationStatus.Uncalculated,
                null, null, [], null, [missing])], [], null, [missing]);
        return SalaryRecord(work, calculation);
    }

    private static WorkRecordDto Record(WorkRecordId id) => new(
        id, WorkDate,
        [
            new WorkTaskDto(new WorkTaskId(id.Value), ServiceId, CategoryId,
                WorkInputMode.TimeRange, new WorkMinutes(60), new MinuteOfDay(540),
                new MinuteOfDay(600), new DisplayOrder(0), null),
        ], null, null);

    private static WorkRecordSalaryDto SalaryRecord(
        WorkRecordDto work,
        WorkSalaryCalculation calculation)
    {
        var calculations = calculation.TaskCalculations.ToDictionary(static value => value.WorkTaskId);
        return new WorkRecordSalaryDto(work, calculation, new YearMonth(2026, 8),
            work.Tasks.Select(task => new WorkTaskSalaryDto(
                task, calculations[task.Id], "訪問介護", "60分")).ToArray());
    }

    private sealed class SalaryStub : ISalaryQueryUseCase
    {
        public Task<HomeSalarySummaryDto> GetHomeSalarySummaryAsync(
            PayrollPeriodKey payrollPeriodKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public required PayrollPeriodSummaryDto Summary { get; init; }
        public PayrollPeriodKey? RequestedKey { get; private set; }
        public WorkRecordId? RequestedWorkRecordId { get; private set; }

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

        public Task<WorkRecordCalculationDto> GetWorkRecordCalculationAsync(
            WorkRecordId workRecordId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedWorkRecordId = workRecordId;
            var record = Summary.Days.SelectMany(x => x.Records)
                .Single(x => x.WorkRecord.Id == workRecordId);
            return Task.FromResult(new WorkRecordCalculationDto(Summary.Period, record));
        }
    }

    private sealed class ContextTrackingReadOnlyList<T>(
        IReadOnlyList<T> items,
        SynchronizationContext uiContext) : IReadOnlyList<T>
    {
        private int accessCount;
        private int uiContextAccessCount;

        public int AccessCount => Volatile.Read(ref accessCount);
        public int UiContextAccessCount => Volatile.Read(ref uiContextAccessCount);
        public int Count
        {
            get
            {
                TrackAccess();
                return items.Count;
            }
        }

        public T this[int index]
        {
            get
            {
                TrackAccess();
                return items[index];
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            TrackAccess();
            return items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private void TrackAccess()
        {
            Interlocked.Increment(ref accessCount);
            if (ReferenceEquals(SynchronizationContext.Current, uiContext))
                Interlocked.Increment(ref uiContextAccessCount);
        }
    }

    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try
                {
                    callback(state);
                }
                finally
                {
                    SetSynchronizationContext(previous);
                }
            });
        }
    }
}
