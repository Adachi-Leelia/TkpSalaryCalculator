namespace TkpSalaryCalculator.App.Tests;

public sealed class CalendarWorkFlowViewModelTests
{
    private static readonly DateOnly TargetDate = new(2026, 8, 21);
    private static readonly ServiceId Service = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));
    private static readonly TimeCategoryId Category = new(Guid.Parse("20000000-0000-0000-0000-000000000001"));
    private static readonly ServicePresetId Preset = new(Guid.Parse("30000000-0000-0000-0000-000000000001"));
    private static readonly WorkRecordId RecordId = new(Guid.Parse("40000000-0000-0000-0000-000000000001"));

    [Fact]
    public async Task UI005_SelectDateStaysOnCalendarAndRefreshesSummary()
    {
        var query = new SalaryQueryStub();
        query.Months[new YearMonth(2026, 8)] = Month(2026, 8, TargetDate, recordCount: 1, uncalculated: 0, shiftCandidates: 2);
        query.Days[TargetDate] = Day(TargetDate, calculated: true);
        var navigator = new CalendarNavigatorStub();
        var session = new AppSessionState(TargetDate);
        var viewModel = new CalendarViewModel(
            query, navigator, session, new ClockStub(), new LocalDateStub(),
            new JapaneseDisplayFormatter(), new UserErrorPresenter());

        await viewModel.LoadAsync();
        await viewModel.SelectDateAsync(TargetDate);

        Assert.Null(navigator.OpenedDay);
        Assert.Equal(TargetDate, viewModel.SelectedDate);
        Assert.Equal("1,200円", viewModel.SelectedTotalText);
        Assert.Equal("勤務記録 1件", viewModel.SelectedRecordCountText);
        Assert.Equal(2, viewModel.SelectedShiftCandidateCount);
        var cell = Assert.Single(viewModel.Days, x => x.Date == TargetDate);
        Assert.True(cell.IsSelected);
        Assert.Contains("勤務あり", cell.StateText);
    }

    [Fact]
    public async Task UI006_CalendarCellUsesTextSymbolsForWorkAndUncalculatedStates()
    {
        var query = new SalaryQueryStub();
        query.Months[new YearMonth(2026, 8)] = Month(2026, 8, TargetDate, 1, 1, 0);
        query.Days[TargetDate] = Day(TargetDate, calculated: false);
        var viewModel = CreateCalendar(query);

        await viewModel.LoadAsync();

        var cell = Assert.Single(viewModel.Days, x => x.Date == TargetDate);
        Assert.True(cell.HasWorkRecords);
        Assert.True(cell.HasUncalculated);
        Assert.Contains("勤務あり", cell.AccessibilityText);
        Assert.Contains("未計算", cell.AccessibilityText);
        Assert.Contains("未計算", viewModel.SelectedUncalculatedText);
    }

    [Fact]
    public async Task UX003_CalendarOpensShiftConfirmationAndAppliesConfirmedCandidatesWithoutOpeningDayPage()
    {
        var query = new SalaryQueryStub();
        query.Months[new YearMonth(2026, 8)] = Month(2026, 8, TargetDate, 0, 0, shiftCandidates: 1);
        query.Days[TargetDate] = EmptyDay(TargetDate);
        var shift = new BasicShiftDto(
            new BasicShiftId(Guid.Parse("70000000-0000-0000-0000-000000000001")), TargetDate.DayOfWeek, null,
            Service, Category, WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0), true);
        var basicShifts = new BasicShiftUseCaseStub
        {
            Preview = new BasicShiftPreviewDto(TargetDate, [new BasicShiftCandidateDto(shift, true, false, false, [])], 0),
        };
        var dialogs = new DialogStub { Result = true };
        var navigator = new CalendarNavigatorStub();
        var work = new WorkUseCaseStub();
        var viewModel = new CalendarViewModel(
            query, navigator, new AppSessionState(TargetDate), new ClockStub(), new LocalDateStub(),
            new JapaneseDisplayFormatter(), new UserErrorPresenter(), basicShifts, work, dialogs);

        await viewModel.LoadAsync();
        await viewModel.ConfirmShiftCandidatesAsync();

        Assert.Equal("基本シフトを反映", dialogs.LastTitle);
        Assert.Contains("追加する勤務記録: 1件", dialogs.LastMessage);
        Assert.Contains("訪問 / 通常", dialogs.LastMessage);
        Assert.Equal(TargetDate, basicShifts.Applied?.WorkDate);
        Assert.Equal([shift.Id], basicShifts.Applied?.BasicShiftIds);
        Assert.Null(navigator.OpenedDay);
        Assert.Equal(1, work.SettingsForDateCalls);
        Assert.Equal(0, work.InputOptionsCalls);
        Assert.Equal(0, query.DayScreenCalls);
        Assert.Equal(2, query.CalendarScreenCalls);
    }

    [Fact]
    public async Task CalendarMonthMoveUpdatesSessionAndSelectedDayTogether()
    {
        var query = new SalaryQueryStub();
        query.Months[new YearMonth(2026, 8)] = Month(2026, 8, TargetDate, 0, 0, 0);
        query.Months[new YearMonth(2026, 9)] = Month(2026, 9, new DateOnly(2026, 9, 1), 0, 0, 0);
        query.Days[TargetDate] = EmptyDay(TargetDate);
        query.Days[new DateOnly(2026, 9, 1)] = EmptyDay(new DateOnly(2026, 9, 1));
        var session = new AppSessionState(TargetDate);
        var viewModel = new CalendarViewModel(
            query, new CalendarNavigatorStub(), session, new ClockStub(), new LocalDateStub(),
            new JapaneseDisplayFormatter(), new UserErrorPresenter());

        await viewModel.LoadAsync();
        await viewModel.MoveMonthAsync(1);

        Assert.Equal(new YearMonth(2026, 9), viewModel.DisplayedMonth);
        Assert.Equal(new YearMonth(2026, 9), session.CalendarMonth);
        Assert.Equal(new DateOnly(2026, 9, 1), session.SelectedCalendarDate);
    }

    [Fact]
    public async Task CalendarMonthMove_DayLoadFailureKeepsPriorMonthAndSelectedDate()
    {
        var nextMonthDate = new DateOnly(2026, 9, 1);
        var query = new SalaryQueryStub();
        query.Months[new YearMonth(2026, 8)] = Month(2026, 8, TargetDate, 0, 0, 0);
        query.Months[new YearMonth(2026, 9)] = Month(2026, 9, nextMonthDate, 0, 0, 0);
        query.Days[TargetDate] = EmptyDay(TargetDate);
        query.DayExceptions[nextMonthDate] = new IOException("day query failed");
        var session = new AppSessionState(TargetDate);
        var viewModel = new CalendarViewModel(
            query, new CalendarNavigatorStub(), session, new ClockStub(), new LocalDateStub(),
            new JapaneseDisplayFormatter(), new UserErrorPresenter());

        await viewModel.LoadAsync();
        await viewModel.MoveMonthAsync(1);

        Assert.True(viewModel.HasError);
        Assert.Equal(new YearMonth(2026, 8), viewModel.DisplayedMonth);
        Assert.Equal(new YearMonth(2026, 8), session.CalendarMonth);
        Assert.Equal(TargetDate, viewModel.SelectedDate);
        Assert.Equal(TargetDate, session.SelectedCalendarDate);
        Assert.True(Assert.Single(viewModel.Days, day => day.Date == TargetDate).IsSelected);
    }

    [Fact]
    public async Task WORK009_DeleteRequiresConfirmationAndReloadsOnlyAfterAcceptance()
    {
        var query = new SalaryQueryStub { Days = { [TargetDate] = Day(TargetDate, calculated: true) } };
        var work = new WorkUseCaseStub();
        work.Stored.Add(Record());
        var dialogs = new DialogStub { Result = false };
        var viewModel = new DayViewModel(
            query, work, new CalendarNavigatorStub(), dialogs,
            new JapaneseDisplayFormatter(), new UserErrorPresenter());
        viewModel.SetDate(TargetDate);
        await viewModel.LoadAsync();

        Assert.Equal("訪問 / 通常", Assert.Single(viewModel.Records).DisplayName);
        Assert.Equal(0, work.SettingsForDateCalls);
        Assert.Equal(0, work.InputOptionsCalls);
        Assert.Equal(1, query.DayScreenCalls);

        await viewModel.DeleteRecordAsync(RecordId, "訪問 / 通常");
        Assert.Empty(work.Deleted);

        dialogs.Result = true;
        await viewModel.DeleteRecordAsync(RecordId, "訪問 / 通常");
        Assert.Equal([RecordId], work.Deleted);
        Assert.True(viewModel.HasSuccessMessage);
        Assert.Equal("勤務記録を削除", dialogs.LastTitle);
    }

    [Fact]
    public async Task DLGCOPY01_PreviewsBeforeConfirmationAndCopiesOnlyAfterAcceptance()
    {
        var sourceDate = TargetDate.AddDays(-2);
        var events = new List<string>();
        var query = new SalaryQueryStub { Days = { [TargetDate] = EmptyDay(TargetDate) } };
        var work = new WorkUseCaseStub
        {
            SharedEvents = events,
            CopyPreview = new CopyDayPreviewDto(
                sourceDate, TargetDate, 2, 1, new YearMonth(2026, 7), new YearMonth(2026, 8), true,
                [new IssueDto("COPY_DAY_TARGET_HAS_RECORDS", null, "重複しないか確認してください。")],
                CopyToken(sourceDate, TargetDate, 1)),
        };
        var dialogs = new DialogStub { Result = false, SharedEvents = events };
        var viewModel = new DayViewModel(
            query, work, new CalendarNavigatorStub(), dialogs,
            new JapaneseDisplayFormatter(), new UserErrorPresenter());
        viewModel.SetDate(TargetDate);
        viewModel.CopySourceDate = sourceDate.ToDateTime(TimeOnly.MinValue);

        await viewModel.CopyDayAsync();

        Assert.Equal(1, work.CopyPreviewCalls);
        Assert.Equal(0, work.CopyCalls);
        Assert.Contains("複製される勤務記録: 2件", dialogs.LastMessage);
        Assert.Contains("複製先の既存勤務記録: 1件", dialogs.LastMessage);
        Assert.Contains("再計算", dialogs.LastMessage);

        dialogs.Result = true;
        await viewModel.CopyDayAsync();

        Assert.Equal(1, work.CopyCalls);
        Assert.Equal(["preview", "dialog", "preview", "dialog", "copy"], events);
        Assert.Equal(sourceDate, work.LastCopySourceDate);
        Assert.Equal(TargetDate, work.LastCopyTargetDate);
        Assert.Equal("勤務記録を2件複製しました。", viewModel.SuccessMessage);
    }

    [Fact]
    public async Task DLGCOPY01_BlockingPreviewNeverCopies()
    {
        var sourceDate = TargetDate.AddDays(-1);
        var work = new WorkUseCaseStub
        {
            CopyPreview = new CopyDayPreviewDto(
                sourceDate, TargetDate, 0, 0, new YearMonth(2026, 8), new YearMonth(2026, 8), false,
                [new IssueDto("COPY_DAY_SOURCE_EMPTY", "SourceDate", "複製元に勤務記録がありません。")],
                CopyToken(sourceDate, TargetDate)),
        };
        var dialogs = new DialogStub { Result = true };
        var viewModel = new DayViewModel(
            new SalaryQueryStub(), work, new CalendarNavigatorStub(), dialogs,
            new JapaneseDisplayFormatter(), new UserErrorPresenter());
        viewModel.SetDate(TargetDate);
        viewModel.CopySourceDate = sourceDate.ToDateTime(TimeOnly.MinValue);

        await viewModel.CopyDayAsync();

        Assert.Equal("複製できません", dialogs.LastTitle);
        Assert.Equal(0, work.CopyCalls);
    }

    [Fact]
    public async Task DLGCOPY01_FutureSourceIsBlockedAndPickerUsesPriorDayAsMaximum()
    {
        var sourceDate = TargetDate.AddDays(1);
        var work = new WorkUseCaseStub
        {
            CopyPreview = new CopyDayPreviewDto(
                sourceDate, TargetDate, 1, 0, new YearMonth(2026, 8), new YearMonth(2026, 8), false,
                [new IssueDto("COPY_DAY_SOURCE_MUST_BE_PAST", "SourceDate", "複製元には複製先より過去の日付を指定してください。")],
                CopyToken(sourceDate, TargetDate)),
        };
        var dialogs = new DialogStub { Result = true };
        var viewModel = new DayViewModel(
            new SalaryQueryStub(), work, new CalendarNavigatorStub(), dialogs,
            new JapaneseDisplayFormatter(), new UserErrorPresenter());
        viewModel.SetDate(TargetDate);
        viewModel.CopySourceDate = sourceDate.ToDateTime(TimeOnly.MinValue);

        await viewModel.CopyDayAsync();

        Assert.Equal(TargetDate.AddDays(-1).ToDateTime(TimeOnly.MinValue), viewModel.CopySourceMaximumDate);
        Assert.Equal("複製できません", dialogs.LastTitle);
        Assert.Equal(0, work.CopyCalls);
    }

    [Fact]
    public async Task DLGCOPY01_ReportsCopySuccessWhenReloadFails()
    {
        var sourceDate = TargetDate.AddDays(-1);
        var query = new SalaryQueryStub { Days = { [TargetDate] = EmptyDay(TargetDate) } };
        var work = new WorkUseCaseStub
        {
            CopyPreview = new CopyDayPreviewDto(
                sourceDate, TargetDate, 1, 0, new YearMonth(2026, 8), new YearMonth(2026, 8), false,
                [], CopyToken(sourceDate, TargetDate)),
        };
        var viewModel = new DayViewModel(
            query, work, new CalendarNavigatorStub(), new DialogStub { Result = true },
            new JapaneseDisplayFormatter(), new UserErrorPresenter());
        viewModel.SetDate(TargetDate);
        await viewModel.LoadAsync();
        query.DayExceptions[TargetDate] = new IOException("reload failed");

        await viewModel.CopyDayAsync();

        Assert.Equal(1, work.CopyCalls);
        Assert.Contains("勤務記録を1件複製しました。", viewModel.SuccessMessage);
        Assert.Contains("再読み込みに失敗", viewModel.SuccessMessage);
        Assert.True(viewModel.HasError);
    }

    [Fact]
    public async Task SCRDAY01_RecordBreakdownPassesDateAndRecordIdToNavigator()
    {
        var query = new SalaryQueryStub { Days = { [TargetDate] = Day(TargetDate, calculated: true) } };
        var work = new WorkUseCaseStub();
        work.Stored.Add(Record());
        var navigator = new CalendarNavigatorStub();
        var viewModel = new DayViewModel(
            query, work, navigator, new DialogStub(), new JapaneseDisplayFormatter(), new UserErrorPresenter());
        viewModel.SetDate(TargetDate);
        await viewModel.LoadAsync();

        await viewModel.OpenCalculationDetailsAsync(Assert.Single(viewModel.Records).Id);

        Assert.Equal(TargetDate, navigator.CalculationDate);
        Assert.Equal(RecordId, navigator.CalculationRecordId);
    }

    [Fact]
    public async Task WorkEditor_InvalidDurationBlocksPreviewAndShowsFieldError()
    {
        var fixture = new EditorFixture();
        await fixture.ViewModel.LoadAsync();
        var previewCalls = fixture.Work.PreviewCalls;

        fixture.ViewModel.WorkMinutesText = "0";
        await fixture.ViewModel.PreviewAsync();

        Assert.False(fixture.ViewModel.CanSave);
        Assert.Equal("WorkMinutes", fixture.ViewModel.FirstInvalidField);
        Assert.Contains("1分以上24時間以内", fixture.ViewModel.WorkMinutesError);
        Assert.Equal(previewCalls, fixture.Work.PreviewCalls);
    }

    [Fact]
    public async Task WORK001And002_PresetFillsStandardValuesAndArbitraryTimeOmitsCategory()
    {
        var fixture = new EditorFixture();
        await fixture.ViewModel.LoadAsync();

        Assert.Equal(Preset, fixture.ViewModel.SelectedPreset?.Id);
        Assert.Equal(Service, fixture.ViewModel.SelectedService?.Id);
        Assert.Equal(Category, fixture.ViewModel.SelectedTimeCategory?.Id);
        Assert.Equal("60", fixture.ViewModel.WorkMinutesText);

        fixture.ViewModel.SelectedTimeCategory = fixture.ViewModel.TimeCategories.Single(x => x.Id is null);
        fixture.ViewModel.WorkMinutesText = "75";
        await fixture.ViewModel.PreviewAsync();

        Assert.Null(fixture.Work.LastPreviewCommand?.TimeCategoryId);
        Assert.Equal(new WorkMinutes(75), fixture.Work.LastPreviewCommand?.WorkMinutes);
    }

    [Fact]
    public async Task WorkEditor_NewRecordFiltersUnavailableCandidatesAndSkipsUnavailableSuggestion()
    {
        var disabledService = new ServiceId(Guid.Parse("10000000-0000-0000-0000-000000000002"));
        var disabledCategory = new TimeCategoryId(Guid.Parse("20000000-0000-0000-0000-000000000002"));
        var unavailablePreset = new ServicePresetId(Guid.Parse("30000000-0000-0000-0000-000000000002"));
        var fixture = new EditorFixture();
        fixture.Work.InputOptions = Options(
            services:
            [
                new SnapshotService(Service, "訪問", new DisplayOrder(0), true),
                new SnapshotService(disabledService, "無効な訪問", new DisplayOrder(1), false),
            ],
            timeCategories:
            [
                new SnapshotTimeCategory(Category, Service, "通常", new WorkMinutes(60), new DisplayOrder(0), true),
                new SnapshotTimeCategory(disabledCategory, Service, "廃止区分", new WorkMinutes(30), new DisplayOrder(1), false),
            ],
            suggestedValues: new SaveWorkRecordCommand(
                null, TargetDate, disabledService, null, WorkInputMode.Duration, new WorkMinutes(60),
                null, null, null, Guid.NewGuid()),
            presetCandidates:
            [
                new ServicePresetCandidateDto(
                    new ServicePresetDto(Preset, "訪問・通常", Service, Category, new WorkMinutes(60), new DisplayOrder(0), true),
                    true, 3, true, []),
                new ServicePresetCandidateDto(
                    new ServicePresetDto(unavailablePreset, "利用不可", disabledService, null, new WorkMinutes(60), new DisplayOrder(1), false),
                    false, 0, false, [new IssueDto("WORK_SERVICE_UNAVAILABLE", null, "この年月では使用できません。")]),
            ]);

        await fixture.ViewModel.LoadAsync();

        Assert.Equal(Service, fixture.ViewModel.SelectedService?.Id);
        Assert.DoesNotContain(fixture.ViewModel.Services, x => x.Id == disabledService);
        Assert.DoesNotContain(fixture.ViewModel.TimeCategories, x => x.Id == disabledCategory);
        Assert.DoesNotContain(fixture.ViewModel.PresetCandidates, x => x.Id == unavailablePreset);
        Assert.Contains("利用できない候補", fixture.ViewModel.UnavailableCandidatesText);
        Assert.True(fixture.ViewModel.CanSave);
    }

    [Fact]
    public async Task WorkEditor_EditKeepsDisabledOriginalSelectionAndSourcePreset()
    {
        var disabledService = new ServiceId(Guid.Parse("10000000-0000-0000-0000-000000000002"));
        var disabledCategory = new TimeCategoryId(Guid.Parse("20000000-0000-0000-0000-000000000002"));
        var unavailablePreset = new ServicePresetId(Guid.Parse("30000000-0000-0000-0000-000000000002"));
        var work = new WorkUseCaseStub();
        work.Stored.Add(new WorkRecordDto(
            RecordId, TargetDate, disabledService, disabledCategory, WorkInputMode.Duration,
            new WorkMinutes(60), null, null, unavailablePreset, null, null));
        work.InputOptions = Options(
            services: [new SnapshotService(disabledService, "無効な訪問", new DisplayOrder(0), false)],
            timeCategories: [new SnapshotTimeCategory(disabledCategory, disabledService, "廃止区分", new WorkMinutes(60), new DisplayOrder(0), false)],
            presetCandidates:
            [
                new ServicePresetCandidateDto(
                    new ServicePresetDto(unavailablePreset, "利用不可", disabledService, disabledCategory, new WorkMinutes(60), new DisplayOrder(0), false),
                    false, 1, true, [new IssueDto("WORK_SERVICE_UNAVAILABLE", null, "この年月では使用できません。")]),
            ]);
        var viewModel = new WorkEditorViewModel(
            work, new CalendarNavigatorStub(), new IssuePresenter(), new JapaneseDisplayFormatter(),
            new UserErrorPresenter(), new DialogStub { Result = true });
        viewModel.Initialize(TargetDate, RecordId);

        await viewModel.LoadAsync();
        await viewModel.SaveAsync();

        Assert.Contains(viewModel.Services, x => x.Id == disabledService);
        Assert.Contains(viewModel.TimeCategories, x => x.Id == disabledCategory);
        Assert.Empty(viewModel.PresetCandidates);
        var saved = Assert.Single(work.Saved);
        Assert.Equal(disabledService, saved.ServiceId);
        Assert.Equal(disabledCategory, saved.TimeCategoryId);
        Assert.Equal(unavailablePreset, saved.SourceServicePresetId);
    }

    [Fact]
    public async Task WorkEditor_UncalculatedPreviewCanStillBeSavedAndReturns()
    {
        var fixture = new EditorFixture();
        fixture.Work.PreviewFactory = command => UncalculatedPreview(command);
        await fixture.ViewModel.LoadAsync();
        var previewCalls = fixture.Work.PreviewCalls;

        Assert.True(fixture.ViewModel.CanSave);
        Assert.Contains("勤務内容は保存できます", fixture.ViewModel.PreviewText);

        await fixture.ViewModel.SaveAsync();

        Assert.Single(fixture.Work.Saved);
        Assert.Equal(previewCalls, fixture.Work.PreviewCalls);
        Assert.Equal(1, fixture.Navigator.GoBackCalls);
        Assert.Equal("勤務記録を保存しました。", fixture.Navigator.GoBackSuccessMessage);
        Assert.False(fixture.ViewModel.IsDirty);
    }

    [Fact]
    public async Task WorkEditor_TimeRangeDisplaysApplicationNormalizedOvernightResult()
    {
        var fixture = new EditorFixture();
        await fixture.ViewModel.LoadAsync();
        fixture.ViewModel.SelectedInputMode = WorkInputModeOption.TimeRange;
        fixture.ViewModel.StartTime = new TimeSpan(23, 0, 0);
        fixture.ViewModel.EndTime = new TimeSpan(1, 0, 0);
        fixture.Work.PreviewFactory = command => new WorkRecordPreviewDto(
            new WorkMinutes(120), new MinuteOfDay(1380), new MinuteOfDay(60),
            Calculated(RecordId, 2_400), true, []);

        await fixture.ViewModel.PreviewAsync();

        Assert.True(fixture.ViewModel.CanSave);
        Assert.Contains("2時間", fixture.ViewModel.NormalizedTimeText);
        Assert.Contains("翌日", fixture.ViewModel.NormalizedTimeText);
    }

    [Fact]
    public async Task WorkEditor_EditPreservesStoredValuesAndSaveUsesExistingIdWithoutExtraPreviewTap()
    {
        var work = new WorkUseCaseStub();
        work.Stored.Add(Record() with { WorkMinutes = new WorkMinutes(90) });
        var navigator = new CalendarNavigatorStub();
        var viewModel = new WorkEditorViewModel(
            work, navigator, new IssuePresenter(), new JapaneseDisplayFormatter(),
            new UserErrorPresenter(), new DialogStub { Result = true });
        viewModel.Initialize(TargetDate, RecordId);

        await viewModel.LoadAsync();
        Assert.Equal("90", viewModel.WorkMinutesText);
        var previewCalls = work.PreviewCalls;

        viewModel.WorkMinutesText = "75";
        Assert.False(viewModel.CanSave);
        await viewModel.SaveAsync();

        var saved = Assert.Single(work.Saved);
        Assert.Equal(RecordId, saved.Id);
        Assert.Equal(new WorkMinutes(75), saved.WorkMinutes);
        Assert.Equal(previewCalls + 1, work.PreviewCalls);
        Assert.Equal(1, navigator.GoBackCalls);
    }

    [Fact]
    public async Task WORK008_SaveFailureKeepsInputsAndDoesNotNavigate()
    {
        var fixture = new EditorFixture();
        await fixture.ViewModel.LoadAsync();
        fixture.ViewModel.WorkMinutesText = "75";
        fixture.Work.SaveException = new IOException("database internal path");

        await fixture.ViewModel.SaveAsync();

        Assert.Equal("75", fixture.ViewModel.WorkMinutesText);
        Assert.True(fixture.ViewModel.IsDirty);
        Assert.True(fixture.ViewModel.HasError);
        Assert.Equal(0, fixture.Navigator.GoBackCalls);
    }

    [Fact]
    public async Task WorkEditor_HolidayOnlyTimedPremiumRequiresStartTimeOnlyOnHoliday()
    {
        var fixture = new EditorFixture();
        fixture.Work.InputOptions = Options([
            new SnapshotPremium(
                new PremiumId(Guid.NewGuid()), "祝日夜間", PremiumCalculationType.FixedPerHour,
                null, new YenAmount(100), new MinuteOfDay(1320), new MinuteOfDay(300), true,
                new HashSet<DayOfWeek>(), new HashSet<DateOnly>(), new HashSet<ServiceId>(), true),
        ]);

        await fixture.ViewModel.LoadAsync();

        Assert.False(fixture.ViewModel.ShowStartTime);
        Assert.Null(fixture.Work.LastPreviewCommand?.StartTime);

        fixture.Holidays.Holidays[TargetDate] = "テスト祝日";
        await fixture.ViewModel.LoadAsync();

        Assert.True(fixture.ViewModel.ShowStartTime);
        Assert.NotNull(fixture.Work.LastPreviewCommand?.StartTime);
    }

    private static CalendarViewModel CreateCalendar(SalaryQueryStub query) => new(
        query, new CalendarNavigatorStub(), new AppSessionState(TargetDate),
        new ClockStub(), new LocalDateStub(), new JapaneseDisplayFormatter(), new UserErrorPresenter());

    private static IReadOnlyList<CalendarDayDto> Month(
        int year, int month, DateOnly specialDate, int recordCount, int uncalculated, int shiftCandidates)
    {
        var count = DateTime.DaysInMonth(year, month);
        return Enumerable.Range(1, count).Select(day =>
        {
            var date = new DateOnly(year, month, day);
            return date == specialDate
                ? new CalendarDayDto(date, recordCount, new YenAmount(recordCount == 0 ? 0 : 1_200), uncalculated, shiftCandidates)
                : new CalendarDayDto(date, 0, new YenAmount(0), 0, 0);
        }).ToArray();
    }

    private static DailySalaryDto Day(DateOnly date, bool calculated)
    {
        var calculation = calculated ? Calculated(RecordId, 1_200) : Uncalculated(RecordId);
        return new DailySalaryDto(
            date,
            [new WorkRecordSalaryDto(Record(), calculation)],
            new YenAmount(calculated ? 1_200 : 0), new YenAmount(0), new YenAmount(0),
            new YenAmount(calculated ? 1_200 : 0), calculated ? 0 : 1);
    }

    private static DailySalaryDto EmptyDay(DateOnly date) => new(
        date, [], new YenAmount(0), new YenAmount(0), new YenAmount(0), new YenAmount(0), 0);

    private static CopyDayConfirmationToken CopyToken(DateOnly sourceDate, DateOnly targetDate, int targetExistingWorkRecordCount = 0) => new(
        sourceDate, targetDate, targetExistingWorkRecordCount,
        new SettingSnapshotId(Guid.Parse("50000000-0000-0000-0000-000000000001")),
        new HolidayCalendarVersionId(Guid.Parse("60000000-0000-0000-0000-000000000001")));

    private static WorkRecordDto Record() => new(
        RecordId, TargetDate, Service, Category, WorkInputMode.Duration,
        new WorkMinutes(60), null, null, Preset, null, null);

    private static WorkSalaryCalculation Calculated(WorkRecordId id, long total) => new(
        id, SalaryCalculationStatus.Calculated, null, new YenAmount(total), [], [], new YenAmount(total), []);

    private static WorkSalaryCalculation Uncalculated(WorkRecordId id) => new(
        id, SalaryCalculationStatus.Uncalculated, null, null, [], [], null,
        [new MissingCalculationRequirement("RATE_REQUIRED", Service.Value)]);

    private static WorkRecordPreviewDto UncalculatedPreview(SaveWorkRecordCommand command) => new(
        command.WorkMinutes ?? new WorkMinutes(60), command.StartTime, command.EndTime,
        Uncalculated(command.Id ?? RecordId), true,
        [new IssueDto("RATE_REQUIRED", null, "基本単価を設定すると給与を計算できます。")]);

    private static WorkInputOptionsDto Options(
        IReadOnlyList<SnapshotPremium>? premiums = null,
        IReadOnlyList<SnapshotService>? services = null,
        IReadOnlyList<SnapshotTimeCategory>? timeCategories = null,
        SaveWorkRecordCommand? suggestedValues = null,
        IReadOnlyList<ServicePresetCandidateDto>? presetCandidates = null)
    {
        var snapshot = new SettingSnapshot(
            new SettingSnapshotId(Guid.Parse("50000000-0000-0000-0000-000000000001")), null,
            new HolidayCalendarVersionId(Guid.Parse("60000000-0000-0000-0000-000000000001")),
            new SchemaVersion(1), DateTimeOffset.UnixEpoch,
            services ?? [new SnapshotService(Service, "訪問", new DisplayOrder(0), true)],
            timeCategories ?? [new SnapshotTimeCategory(Category, Service, "通常", new WorkMinutes(60), new DisplayOrder(0), true)],
            [], premiums ?? [], []);
        var preset = new ServicePresetDto(Preset, "訪問・通常", Service, Category, new WorkMinutes(60), new DisplayOrder(0), true);
        return new WorkInputOptionsDto(
            TargetDate, new MonthSettingsDto(new YearMonth(2026, 8), snapshot),
            presetCandidates ?? [new ServicePresetCandidateDto(preset, true, 3, true, [])], suggestedValues);
    }

    private sealed class EditorFixture
    {
        public EditorFixture()
        {
            Work.HolidayDates = Holidays.Holidays;
            ViewModel = new WorkEditorViewModel(
                Work, Navigator, new IssuePresenter(), new JapaneseDisplayFormatter(),
                new UserErrorPresenter(), new DialogStub { Result = true });
            ViewModel.Initialize(TargetDate, null);
        }
        public WorkUseCaseStub Work { get; } = new();
        public HolidayCalendarStub Holidays { get; } = new();
        public CalendarNavigatorStub Navigator { get; } = new();
        public WorkEditorViewModel ViewModel { get; }
    }

    private sealed class SalaryQueryStub : ISalaryQueryUseCase
    {
        public Dictionary<YearMonth, IReadOnlyList<CalendarDayDto>> Months { get; } = [];
        public Dictionary<DateOnly, DailySalaryDto> Days { get; init; } = [];
        public Dictionary<DateOnly, Exception> DayExceptions { get; } = [];
        public int CalendarScreenCalls { get; private set; }
        public int DayScreenCalls { get; private set; }
        public Task<CalendarMonthScreenDto> GetCalendarMonthScreenAsync(
            YearMonth yearMonth, DateOnly selectedDate, CancellationToken cancellationToken)
        {
            CalendarScreenCalls++;
            if (DayExceptions.TryGetValue(selectedDate, out var exception))
                return Task.FromException<CalendarMonthScreenDto>(exception);
            return Task.FromResult(new CalendarMonthScreenDto(
                Months[yearMonth], Days.GetValueOrDefault(selectedDate, EmptyDay(selectedDate))));
        }
        public Task<DayScreenDto> GetDayScreenAsync(DateOnly workDate, CancellationToken cancellationToken)
        {
            DayScreenCalls++;
            if (DayExceptions.TryGetValue(workDate, out var exception))
                return Task.FromException<DayScreenDto>(exception);
            return Task.FromResult(new DayScreenDto(
                Days.GetValueOrDefault(workDate, EmptyDay(workDate)),
                Options().Settings with { YearMonth = new YearMonth(workDate.Year, workDate.Month) },
                new BasicShiftPreviewDto(workDate, [], Days.GetValueOrDefault(workDate, EmptyDay(workDate)).Records.Count)));
        }
        public Task<IReadOnlyList<CalendarDayDto>> GetCalendarMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken) =>
            Task.FromResult(Months[yearMonth]);
        public Task<DailySalaryDto> GetDayAsync(DateOnly workDate, CancellationToken cancellationToken) =>
            DayExceptions.TryGetValue(workDate, out var exception)
                ? Task.FromException<DailySalaryDto>(exception)
                : Task.FromResult(Days.GetValueOrDefault(workDate, EmptyDay(workDate)));
        public Task<PayrollPeriodSummaryDto> GetPayrollPeriodAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class WorkUseCaseStub : IWorkRecordUseCase
    {
        public List<WorkRecordDto> Stored { get; } = [];
        public List<WorkRecordId> Deleted { get; } = [];
        public List<SaveWorkRecordCommand> Saved { get; } = [];
        public int PreviewCalls { get; private set; }
        public SaveWorkRecordCommand? LastPreviewCommand { get; private set; }
        public Exception? SaveException { get; set; }
        public CopyDayPreviewDto? CopyPreview { get; set; }
        public int CopyCalls { get; private set; }
        public int CopyPreviewCalls { get; private set; }
        public int InputOptionsCalls { get; private set; }
        public int SettingsForDateCalls { get; private set; }
        public DateOnly? LastCopySourceDate { get; private set; }
        public DateOnly? LastCopyTargetDate { get; private set; }
        public CopyDayConfirmationToken? LastCopyConfirmationToken { get; private set; }
        public List<string>? SharedEvents { get; init; }
        public WorkInputOptionsDto InputOptions { get; set; } = Options();
        public IReadOnlyDictionary<DateOnly, string> HolidayDates { get; set; } = new Dictionary<DateOnly, string>();
        public Func<SaveWorkRecordCommand, WorkRecordPreviewDto> PreviewFactory { get; set; } = command => new(
            command.WorkMinutes ?? new WorkMinutes(60), command.StartTime, command.EndTime,
            Calculated(command.Id ?? RecordId, 1_200), true, []);

        public async Task<WorkEditorScreenDto> GetEditorScreenAsync(
            DateOnly workDate, WorkRecordId? workRecordId, CancellationToken cancellationToken)
        {
            var options = await GetInputOptionsAsync(workDate, cancellationToken);
            var existing = workRecordId is null ? null : Stored.FirstOrDefault(x => x.Id == workRecordId);
            return new(options, existing,
                new HolidayCalendar(options.Settings.Snapshot.HolidayCalendarVersionId, HolidayDates));
        }

        public Task<MonthSettingsDto> GetSettingsForDateAsync(DateOnly workDate, CancellationToken cancellationToken)
        {
            SettingsForDateCalls++;
            return Task.FromResult(InputOptions.Settings with { YearMonth = new YearMonth(workDate.Year, workDate.Month) });
        }
        public Task<WorkInputOptionsDto> GetInputOptionsAsync(DateOnly workDate, CancellationToken cancellationToken)
        {
            InputOptionsCalls++;
            return Task.FromResult(InputOptions with
            {
                WorkDate = workDate,
                Settings = InputOptions.Settings with { YearMonth = new YearMonth(workDate.Year, workDate.Month) },
            });
        }
        public Task<IReadOnlyList<WorkRecordDto>> GetForDateAsync(DateOnly workDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkRecordDto>>(Stored.Where(x => x.WorkDate == workDate).ToArray());
        public Task<WorkRecordPreviewDto> PreviewAsync(SaveWorkRecordCommand command, CancellationToken cancellationToken)
        {
            PreviewCalls++;
            LastPreviewCommand = command;
            return Task.FromResult(PreviewFactory(command));
        }
        public Task<WorkRecordPreviewDto> PreviewForEditorAsync(
            SaveWorkRecordCommand command, WorkEditorScreenDto screen, CancellationToken cancellationToken) =>
            PreviewAsync(command, cancellationToken);
        public Task<SaveWorkRecordResultDto> SaveAsync(SaveWorkRecordCommand command, CancellationToken cancellationToken)
        {
            if (SaveException is not null) throw SaveException;
            Saved.Add(command);
            var record = new WorkRecordDto(
                command.Id ?? RecordId, command.WorkDate, command.ServiceId, command.TimeCategoryId,
                command.InputMode, command.WorkMinutes ?? new WorkMinutes(60), command.StartTime,
                command.EndTime, command.SourceServicePresetId, null, null);
            return Task.FromResult(new SaveWorkRecordResultDto(record, PreviewFactory(command).Calculation!, []));
        }
        public Task DeleteAsync(WorkRecordId id, CancellationToken cancellationToken)
        {
            Deleted.Add(id);
            Stored.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }
        public Task<CopyDayPreviewDto> PreviewCopyDayAsync(DateOnly sourceDate, DateOnly targetDate, CancellationToken cancellationToken)
        {
            CopyPreviewCalls++;
            SharedEvents?.Add("preview");
            return Task.FromResult(CopyPreview ?? new CopyDayPreviewDto(
                sourceDate, targetDate, Stored.Count(x => x.WorkDate == sourceDate), Stored.Count(x => x.WorkDate == targetDate),
                new YearMonth(sourceDate.Year, sourceDate.Month), new YearMonth(targetDate.Year, targetDate.Month),
                sourceDate.Year != targetDate.Year || sourceDate.Month != targetDate.Month, [],
                CopyToken(sourceDate, targetDate, Stored.Count(x => x.WorkDate == targetDate))));
        }
        public Task<IReadOnlyList<SaveWorkRecordResultDto>> CopyDayAsync(DateOnly sourceDate, DateOnly targetDate,
            CopyDayConfirmationToken confirmationToken, CancellationToken cancellationToken)
        {
            CopyCalls++;
            SharedEvents?.Add("copy");
            LastCopySourceDate = sourceDate;
            LastCopyTargetDate = targetDate;
            LastCopyConfirmationToken = confirmationToken;
            var count = CopyPreview?.SourceWorkRecordCount ?? Stored.Count(x => x.WorkDate == sourceDate);
            IReadOnlyList<SaveWorkRecordResultDto> results = Enumerable.Range(0, count).Select(_ =>
            {
                var record = Record() with { Id = new WorkRecordId(Guid.NewGuid()), WorkDate = targetDate, SourceWorkRecordId = RecordId };
                Stored.Add(record);
                return new SaveWorkRecordResultDto(record, Calculated(record.Id, 1_200), []);
            }).ToArray();
            return Task.FromResult(results);
        }
    }

    private sealed class CalendarNavigatorStub : ICalendarNavigator
    {
        public DateOnly? OpenedDay { get; private set; }
        public DateOnly? CalculationDate { get; private set; }
        public WorkRecordId? CalculationRecordId { get; private set; }
        public int GoBackCalls { get; private set; }
        public string? GoBackSuccessMessage { get; private set; }
        public Task OpenDayAsync(DateOnly date, CancellationToken cancellationToken) { OpenedDay = date; return Task.CompletedTask; }
        public Task OpenWorkEditorAsync(DateOnly date, WorkRecordId? workRecordId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenCalculationDetailsAsync(DateOnly date, WorkRecordId workRecordId, CancellationToken cancellationToken)
        {
            CalculationDate = date;
            CalculationRecordId = workRecordId;
            return Task.CompletedTask;
        }
        public Task GoBackAsync(string? successMessage, CancellationToken cancellationToken)
        {
            GoBackCalls++;
            GoBackSuccessMessage = successMessage;
            return Task.CompletedTask;
        }
    }

    private sealed class BasicShiftUseCaseStub : IBasicShiftUseCase
    {
        public BasicShiftPreviewDto? Preview { get; init; }
        public ApplyBasicShiftsCommand? Applied { get; private set; }

        public Task<IReadOnlyList<BasicShiftDto>> GetForWeekdayAsync(DayOfWeek weekday, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BasicShiftDto>>([]);
        public Task<BasicShiftDto> SaveAsync(SaveBasicShiftCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(BasicShiftId id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BasicShiftPreviewDto> PreviewForDateAsync(DateOnly workDate, CancellationToken cancellationToken) =>
            Task.FromResult(Preview ?? new BasicShiftPreviewDto(workDate, [], 0));
        public Task<IReadOnlyList<SaveWorkRecordResultDto>> ApplyAsync(ApplyBasicShiftsCommand command, CancellationToken cancellationToken)
        {
            Applied = command;
            return Task.FromResult<IReadOnlyList<SaveWorkRecordResultDto>>([]);
        }
    }

    private sealed class HolidayCalendarStub : IHolidayCalendarRepository
    {
        public Dictionary<DateOnly, string> Holidays { get; } = [];

        public Task<HolidayCalendar> GetAsync(HolidayCalendarVersionId versionId, CancellationToken cancellationToken) =>
            Task.FromResult(new HolidayCalendar(versionId, Holidays));

        public Task<IReadOnlyDictionary<HolidayCalendarVersionId, HolidayCalendar>> GetManyAsync(
            IReadOnlyCollection<HolidayCalendarVersionId> versionIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<HolidayCalendarVersionId, HolidayCalendar>>(
                versionIds.Distinct().ToDictionary(x => x, x => new HolidayCalendar(x, Holidays)));

        public Task<HolidayCalendarVersionId> GetLatestVerifiedVersionIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HolidayCalendarVersionId(Guid.NewGuid()));
    }

    private sealed class DialogStub : IConfirmationDialogService
    {
        public bool Result { get; set; }
        public string? LastTitle { get; private set; }
        public string? LastMessage { get; private set; }
        public List<string>? SharedEvents { get; init; }
        public Task<bool> ConfirmDiscardChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result);
        public Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancelText, CancellationToken cancellationToken = default)
        {
            LastTitle = title;
            LastMessage = message;
            SharedEvents?.Add("dialog");
            return Task.FromResult(Result);
        }
    }

    private sealed class ClockStub : IUtcClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 20, 15, 0, 0, TimeSpan.Zero);
    }

    private sealed class LocalDateStub : ILocalDateConverter
    {
        public DateOnly ToLocalDate(DateTimeOffset utcDateTime) => TargetDate;
    }
}
