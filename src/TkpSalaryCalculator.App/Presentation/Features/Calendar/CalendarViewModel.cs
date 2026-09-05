using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Calendar;

/// <summary>SCR-CAL-01 の表示月、選択日、および日別サマリーを管理します。</summary>
public sealed class CalendarViewModel : ViewModelBase
{
    private readonly ISalaryQueryUseCase salaryQuery;
    private readonly ICalendarNavigator navigator;
    private readonly IAppSessionState sessionState;
    private readonly IUtcClock clock;
    private readonly ILocalDateConverter localDates;
    private readonly JapaneseDisplayFormatter formatter;
    private readonly IBasicShiftUseCase? basicShifts;
    private readonly IWorkRecordUseCase? workRecords;
    private IReadOnlyList<CalendarDayCellViewModel> days = [];
    private IReadOnlyList<CalendarWorkSummaryRow> selectedWorkRows = [];
    private IReadOnlyList<ShiftCandidateRowViewModel> shiftCandidates = [];
    private YearMonth displayedMonth;
    private DateOnly? selectedDate;
    private DateOnly? shiftPreviewDate;
    private string selectedDateText = string.Empty;
    private string selectedTotalText = "0円";
    private string selectedRecordCountText = "勤務記録はありません";
    private string selectedUncalculatedText = string.Empty;
    private string shiftExistingWorkText = string.Empty;
    private int selectedShiftCandidateCount;
    private bool isShiftConfirmationVisible;

    public CalendarViewModel(
        ISalaryQueryUseCase salaryQuery,
        ICalendarNavigator navigator,
        IAppSessionState sessionState,
        IUtcClock clock,
        ILocalDateConverter localDates,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter,
        IBasicShiftUseCase? basicShifts = null,
        IWorkRecordUseCase? workRecords = null) : base(errorPresenter)
    {
        this.salaryQuery = salaryQuery ?? throw new ArgumentNullException(nameof(salaryQuery));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.localDates = localDates ?? throw new ArgumentNullException(nameof(localDates));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        this.basicShifts = basicShifts;
        this.workRecords = workRecords;
        displayedMonth = sessionState.CalendarMonth;
        TrackDataChanges(sessionState,
            AppDataChangeKind.WorkRecords | AppDataChangeKind.Settings | AppDataChangeKind.BasicShifts);

        PreviousMonthCommand = new AsyncCommand(() => MoveMonthAsync(-1), PresentError);
        CurrentMonthCommand = new AsyncCommand(MoveToCurrentMonthAsync, PresentError);
        NextMonthCommand = new AsyncCommand(() => MoveMonthAsync(1), PresentError);
        ReloadCommand = new AsyncCommand(LoadAsync, PresentError);
        OpenDayCommand = new AsyncCommand(OpenSelectedDayAsync, PresentError, () => SelectedDate is not null);
        AddWorkCommand = new AsyncCommand(AddWorkAsync, PresentError, () => SelectedDate is not null);
        ConfirmShiftCandidatesCommand = new AsyncCommand(ConfirmShiftCandidatesAsync, PresentError,
            () => SelectedDate is not null && HasShiftCandidates && basicShifts is not null);
        ApplySelectedShiftsCommand = new AsyncCommand(ApplySelectedShiftsAsync, PresentError,
            () => IsShiftConfirmationVisible && ShiftCandidates.Any(x => x.CanChoose && x.IsSelected));
        CancelShiftConfirmationCommand = new AsyncCommand(() =>
        {
            CancelShiftConfirmation();
            return Task.CompletedTask;
        }, PresentError);
    }

    public IReadOnlyList<CalendarDayCellViewModel> Days
    {
        get => days;
        private set => SetProperty(ref days, value);
    }

    public IReadOnlyList<CalendarWorkSummaryRow> SelectedWorkRows
    {
        get => selectedWorkRows;
        private set
        {
            if (!SetProperty(ref selectedWorkRows, value)) return;
            OnPropertyChanged(nameof(HasSelectedWorkRows));
        }
    }

    public bool HasSelectedWorkRows => SelectedWorkRows.Count != 0;

    public IReadOnlyList<ShiftCandidateRowViewModel> ShiftCandidates
    {
        get => shiftCandidates;
        private set
        {
            if (!SetProperty(ref shiftCandidates, value)) return;
            ApplySelectedShiftsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ShiftSelectedVisitCountText));
        }
    }

    public string ShiftSelectedVisitCountText => $"反映予定の訪問: {ShiftCandidates.Count(x => x.CanChoose && x.IsSelected)}件";

    public bool IsShiftConfirmationVisible
    {
        get => isShiftConfirmationVisible;
        private set
        {
            if (!SetProperty(ref isShiftConfirmationVisible, value)) return;
            ApplySelectedShiftsCommand.NotifyCanExecuteChanged();
        }
    }

    public string ShiftExistingWorkText
    {
        get => shiftExistingWorkText;
        private set => SetProperty(ref shiftExistingWorkText, value);
    }

    public YearMonth DisplayedMonth
    {
        get => displayedMonth;
        private set
        {
            if (!SetProperty(ref displayedMonth, value)) return;
            OnPropertyChanged(nameof(MonthText));
        }
    }

    public string MonthText => formatter.Month(DisplayedMonth);

    public DateOnly? SelectedDate
    {
        get => selectedDate;
        private set
        {
            if (!SetProperty(ref selectedDate, value)) return;
            OpenDayCommand.NotifyCanExecuteChanged();
            AddWorkCommand.NotifyCanExecuteChanged();
            ConfirmShiftCandidatesCommand.NotifyCanExecuteChanged();
        }
    }

    public string SelectedDateText
    {
        get => selectedDateText;
        private set => SetProperty(ref selectedDateText, value);
    }

    public string SelectedTotalText
    {
        get => selectedTotalText;
        private set => SetProperty(ref selectedTotalText, value);
    }

    public string SelectedRecordCountText
    {
        get => selectedRecordCountText;
        private set => SetProperty(ref selectedRecordCountText, value);
    }

    public string SelectedUncalculatedText
    {
        get => selectedUncalculatedText;
        private set
        {
            if (!SetProperty(ref selectedUncalculatedText, value)) return;
            OnPropertyChanged(nameof(HasSelectedUncalculated));
        }
    }

    public bool HasSelectedUncalculated => !string.IsNullOrWhiteSpace(SelectedUncalculatedText);

    public int SelectedShiftCandidateCount
    {
        get => selectedShiftCandidateCount;
        private set
        {
            if (!SetProperty(ref selectedShiftCandidateCount, value)) return;
            OnPropertyChanged(nameof(HasShiftCandidates));
            OnPropertyChanged(nameof(ShiftCandidateText));
            ConfirmShiftCandidatesCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasShiftCandidates => SelectedShiftCandidateCount > 0;

    public string ShiftCandidateText => $"基本シフト候補 {SelectedShiftCandidateCount}件";

    public AsyncCommand PreviousMonthCommand { get; }
    public AsyncCommand CurrentMonthCommand { get; }
    public AsyncCommand NextMonthCommand { get; }
    public AsyncCommand ReloadCommand { get; }
    public AsyncCommand OpenDayCommand { get; }
    public AsyncCommand AddWorkCommand { get; }
    public AsyncCommand ConfirmShiftCandidatesCommand { get; }
    public AsyncCommand ApplySelectedShiftsCommand { get; }
    public AsyncCommand CancelShiftConfirmationCommand { get; }

    public Task LoadAsync() => LoadTrackedAsync(token => LoadMonthCoreAsync(sessionState.CalendarMonth, token), force: true);

    public Task LoadIfNeededAsync() =>
        LoadTrackedAsync(token => LoadMonthCoreAsync(sessionState.CalendarMonth, token), force: false);

    public Task MoveMonthAsync(int months) =>
        LoadTrackedAsync(token => LoadMonthCoreAsync(DisplayedMonth.AddMonths(months), token), force: true);

    public Task MoveToCurrentMonthAsync()
    {
        var today = localDates.ToLocalDate(clock.UtcNow);
        return LoadTrackedAsync(
            token => LoadMonthCoreAsync(new YearMonth(today.Year, today.Month), token, today), force: true);
    }

    public Task SelectDateAsync(DateOnly date) => RunBusyAsync(token => SelectDateCoreAsync(date, token));

    public Task OpenSelectedDayAsync() => SelectedDate is { } date
        ? navigator.OpenDayAsync(date, CancellationToken.None)
        : Task.CompletedTask;

    public Task AddWorkAsync() => SelectedDate is { } date
        ? navigator.OpenWorkEditorAsync(date, null, CancellationToken.None)
        : Task.CompletedTask;

    /// <summary>DLG-SHIFT-01 をカレンダー上に開き、候補ごとの選択を保存せずに準備します。</summary>
    public Task ConfirmShiftCandidatesAsync() => RunBusyAsync(async cancellationToken =>
    {
        if (SelectedDate is not { } date || basicShifts is null) return;

        var preview = await basicShifts.PreviewForDateAsync(date, cancellationToken);
        var serviceNames = new Dictionary<ServiceId, string>();
        var categoryNames = new Dictionary<TimeCategoryId, string>();
        if (workRecords is not null)
        {
            var monthSettings = await workRecords.GetSettingsForDateAsync(date, cancellationToken);
            serviceNames = monthSettings.Snapshot.Services.ToDictionary(x => x.Id, x => x.DisplayName);
            categoryNames = monthSettings.Snapshot.TimeCategories.ToDictionary(x => x.Id, x => x.DisplayName);
        }

        ShiftCandidates = preview.Candidates.Select(candidate =>
        {
            var shift = candidate.Shift;
            var (name, time) = BasicShiftDisplay.Summarize(shift, serviceNames, categoryNames, formatter);
            var row = new ShiftCandidateRowViewModel(
                shift.Id, name, time, candidate.CanApply,
                candidate.CanApply && !candidate.HasSimilarManualRecord,
                string.Join(Environment.NewLine, candidate.Issues.Select(x => x.Message)));
            row.SelectionChanged += (_, _) =>
            {
                ApplySelectedShiftsCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(ShiftSelectedVisitCountText));
            };
            return row;
        }).ToArray();
        ShiftExistingWorkText = preview.ExistingWorkRecordCount == 0
            ? "既存の勤務記録はありません。"
            : $"既存の勤務記録 {preview.ExistingWorkRecordCount}件";
        shiftPreviewDate = preview.WorkDate;
        IsShiftConfirmationVisible = true;
    });

    public Task ApplySelectedShiftsAsync() => RunBusyAsync(async cancellationToken =>
    {
        if (basicShifts is null || shiftPreviewDate is not { } date || date != SelectedDate) return;
        var selectedIds = ShiftCandidates
            .Where(x => x.CanChoose && x.IsSelected)
            .Select(x => x.Id)
            .ToArray();
        if (selectedIds.Length == 0) return;

        await basicShifts.ApplyAsync(new ApplyBasicShiftsCommand(date, selectedIds), cancellationToken);
        sessionState.NotifyDataChanged(AppDataChangeKind.WorkRecords | AppDataChangeKind.BackupStatus);
        var generation = CaptureTrackedDataGeneration();
        CloseShiftConfirmation();
        await LoadMonthCoreAsync(DisplayedMonth, cancellationToken, date);
        AcceptDataGeneration(generation);
    });

    public bool CancelShiftConfirmation()
    {
        if (!IsShiftConfirmationVisible) return false;
        CloseShiftConfirmation();
        return true;
    }

    private async Task LoadMonthCoreAsync(
        YearMonth month,
        CancellationToken cancellationToken,
        DateOnly? preferredDate = null)
    {
        var target = preferredDate
            ?? (sessionState.SelectedCalendarDate is { } remembered &&
                remembered.Year == month.Year && remembered.Month == month.Month
                    ? remembered
                    : new DateOnly(month.Year, month.Month, 1));

        var screen = await salaryQuery.GetCalendarMonthScreenAsync(month, target, cancellationToken);
        DisplayedMonth = month;
        sessionState.CalendarMonth = month;
        ApplySelectedDate(target, screen.SelectedDay, screen.Days);
    }

    private async Task SelectDateCoreAsync(
        DateOnly date,
        CancellationToken cancellationToken,
        IReadOnlyList<CalendarDayDto>? monthValues = null)
    {
        if (date.Year != DisplayedMonth.Year || date.Month != DisplayedMonth.Month)
            throw new ArgumentOutOfRangeException(nameof(date), "表示月の日付を選択してください。");

        var daily = await salaryQuery.GetDayAsync(date, cancellationToken);
        ApplySelectedDate(date, daily, monthValues ?? Days.Where(x => x.Date is not null).Select(x => x.Source).ToArray());
    }

    private void ApplySelectedDate(
        DateOnly date,
        DailySalaryDto daily,
        IReadOnlyList<CalendarDayDto> monthValues)
    {
        CloseShiftConfirmation();
        SelectedDate = date;
        sessionState.SelectedCalendarDate = date;
        SelectedDateText = formatter.Date(date);
        SelectedTotalText = formatter.Money(daily.CalculatedSubtotal);
        SelectedRecordCountText = daily.Records.Count == 0 ? "勤務記録はありません" : $"勤務記録 {daily.Records.Count}件";
        SelectedUncalculatedText = daily.UncalculatedCount == 0 ? string.Empty : $"未計算 {daily.UncalculatedCount}件";
        SelectedWorkRows = daily.Records.Take(3).Select(value =>
        {
            var orderedTasks = value.Tasks.OrderBy(task => task.WorkTask.DisplayOrder.Value).ToArray();
            var taskSummary = string.Join("、", orderedTasks.Select(FormatTaskName));
            var taskCountText = $"タスク {orderedTasks.Length}件";
            var durationText = string.Join("、", orderedTasks.Select(task => formatter.Duration(task.WorkTask.WorkMinutes)));
            var amountText = value.Calculation.Status == SalaryCalculationStatus.Calculated && value.Calculation.Total is { } total
                ? formatter.Money(total)
                : "未計算";
            return new CalendarWorkSummaryRow(
                taskSummary,
                $"{taskCountText} / {durationText}",
                amountText,
                $"訪問1件、{taskCountText}、{taskSummary}、訪問合計{amountText}");
        }).ToArray();

        SelectedShiftCandidateCount = monthValues.FirstOrDefault(x => x.Date == date)?.BasicShiftCandidateCount ?? 0;
        BuildCells(monthValues, date);
    }

    private static string FormatTaskName(WorkTaskSalaryDto task)
    {
        var service = task.ServiceDisplayName ?? "現在の設定にないサービス";
        return string.IsNullOrWhiteSpace(task.TimeCategoryDisplayName)
            ? service
            : $"{service} / {task.TimeCategoryDisplayName}";
    }

    private void BuildCells(IReadOnlyList<CalendarDayDto> values, DateOnly selected)
    {
        var first = new DateOnly(DisplayedMonth.Year, DisplayedMonth.Month, 1);
        var leading = (int)first.DayOfWeek;
        var cellCount = ((leading + values.Count + 6) / 7) * 7;
        var today = localDates.ToLocalDate(clock.UtcNow);
        var result = new List<CalendarDayCellViewModel>(cellCount);
        for (var index = 0; index < cellCount; index++)
        {
            var dayIndex = index - leading;
            if (dayIndex < 0 || dayIndex >= values.Count)
            {
                result.Add(CalendarDayCellViewModel.Placeholder());
                continue;
            }

            var value = values[dayIndex];
            result.Add(new CalendarDayCellViewModel(
                value,
                value.Date == selected,
                value.Date == today,
                () => SelectDateAsync(value.Date),
                PresentError));
        }
        Days = result;
    }

    private void CloseShiftConfirmation()
    {
        IsShiftConfirmationVisible = false;
        ShiftCandidates = [];
        ShiftExistingWorkText = string.Empty;
        shiftPreviewDate = null;
    }
}

public sealed class CalendarDayCellViewModel
{
    private CalendarDayCellViewModel()
    {
        Source = new CalendarDayDto(default, 0, new YenAmount(0), 0, 0);
        SelectCommand = new AsyncCommand(() => Task.CompletedTask, _ => { }, () => false);
        IsPlaceholder = true;
    }

    public CalendarDayCellViewModel(
        CalendarDayDto source,
        bool isSelected,
        bool isToday,
        Func<Task> select,
        Action<Exception> onException)
    {
        Source = source;
        IsSelected = isSelected;
        IsToday = isToday;
        SelectCommand = new AsyncCommand(select, onException);
    }

    public CalendarDayDto Source { get; }
    public DateOnly? Date => IsPlaceholder ? null : Source.Date;
    public bool IsPlaceholder { get; }
    public bool IsSelected { get; }
    public bool IsToday { get; }
    public bool HasWorkRecords => Source.WorkRecordCount > 0;
    public bool HasUncalculated => Source.UncalculatedCount > 0;
    public string DayText => IsPlaceholder ? string.Empty : Source.Date.Day.ToString();
    public string StateText => IsPlaceholder ? string.Empty : string.Join("・", new[]
    {
        IsToday ? "本日" : null,
        IsSelected ? "選択中" : null,
        HasWorkRecords ? "勤務あり" : null,
        HasUncalculated ? "未計算" : null,
    }.Where(x => x is not null));
    public string DisplayText => IsPlaceholder
        ? string.Empty
        : HasUncalculated ? $"{DayText}  !" : HasWorkRecords ? $"{DayText}  ●" : DayText;
    public string AccessibilityText => IsPlaceholder ? string.Empty : $"{Source.Date:M月d日}、{(string.IsNullOrEmpty(StateText) ? "勤務なし" : StateText)}";
    public AsyncCommand SelectCommand { get; }

    public static CalendarDayCellViewModel Placeholder() => new();
}

public sealed record CalendarWorkSummaryRow(
    string TaskSummaryText,
    string TaskCountAndDurationText,
    string AmountText,
    string AccessibilityText);
