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
    private IReadOnlyList<CalendarDayCellViewModel> days = [];
    private IReadOnlyList<CalendarWorkSummaryRow> selectedWorkRows = [];
    private YearMonth displayedMonth;
    private DateOnly? selectedDate;
    private string selectedDateText = string.Empty;
    private string selectedTotalText = "0円";
    private string selectedRecordCountText = "勤務記録はありません";
    private string selectedUncalculatedText = string.Empty;
    private int selectedShiftCandidateCount;

    public CalendarViewModel(
        ISalaryQueryUseCase salaryQuery,
        ICalendarNavigator navigator,
        IAppSessionState sessionState,
        IUtcClock clock,
        ILocalDateConverter localDates,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        this.salaryQuery = salaryQuery ?? throw new ArgumentNullException(nameof(salaryQuery));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.localDates = localDates ?? throw new ArgumentNullException(nameof(localDates));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        displayedMonth = sessionState.CalendarMonth;

        PreviousMonthCommand = new AsyncCommand(() => MoveMonthAsync(-1), PresentError);
        CurrentMonthCommand = new AsyncCommand(MoveToCurrentMonthAsync, PresentError);
        NextMonthCommand = new AsyncCommand(() => MoveMonthAsync(1), PresentError);
        ReloadCommand = new AsyncCommand(LoadAsync, PresentError);
        OpenDayCommand = new AsyncCommand(OpenSelectedDayAsync, PresentError, () => SelectedDate is not null);
        AddWorkCommand = new AsyncCommand(AddWorkAsync, PresentError, () => SelectedDate is not null);
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

    public Task LoadAsync() => RunBusyAsync(token => LoadMonthCoreAsync(sessionState.CalendarMonth, token));

    public Task MoveMonthAsync(int months) =>
        RunBusyAsync(token => LoadMonthCoreAsync(DisplayedMonth.AddMonths(months), token));

    public Task MoveToCurrentMonthAsync()
    {
        var today = localDates.ToLocalDate(clock.UtcNow);
        return RunBusyAsync(token => LoadMonthCoreAsync(new YearMonth(today.Year, today.Month), token, today));
    }

    public Task SelectDateAsync(DateOnly date) => RunBusyAsync(token => SelectDateCoreAsync(date, token));

    public Task OpenSelectedDayAsync() => SelectedDate is { } date
        ? navigator.OpenDayAsync(date, CancellationToken.None)
        : Task.CompletedTask;

    public Task AddWorkAsync() => SelectedDate is { } date
        ? navigator.OpenWorkEditorAsync(date, null, CancellationToken.None)
        : Task.CompletedTask;

    private async Task LoadMonthCoreAsync(
        YearMonth month,
        CancellationToken cancellationToken,
        DateOnly? preferredDate = null)
    {
        var values = await salaryQuery.GetCalendarMonthAsync(month, cancellationToken);
        var target = preferredDate
            ?? (sessionState.SelectedCalendarDate is { } remembered &&
                remembered.Year == month.Year && remembered.Month == month.Month
                    ? remembered
                    : new DateOnly(month.Year, month.Month, 1));

        var daily = await salaryQuery.GetDayAsync(target, cancellationToken);
        DisplayedMonth = month;
        sessionState.CalendarMonth = month;
        ApplySelectedDate(target, daily, values);
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
        SelectedDate = date;
        sessionState.SelectedCalendarDate = date;
        SelectedDateText = formatter.Date(date);
        SelectedTotalText = formatter.Money(daily.CalculatedSubtotal);
        SelectedRecordCountText = daily.Records.Count == 0 ? "勤務記録はありません" : $"勤務記録 {daily.Records.Count}件";
        SelectedUncalculatedText = daily.UncalculatedCount == 0 ? string.Empty : $"未計算 {daily.UncalculatedCount}件";
        SelectedWorkRows = daily.Records.Take(3).Select(value => new CalendarWorkSummaryRow(
            formatter.Duration(value.WorkRecord.WorkMinutes),
            value.Calculation.Status == SalaryCalculationStatus.Calculated && value.Calculation.Total is { } total
                ? formatter.Money(total)
                : "未計算")).ToArray();

        SelectedShiftCandidateCount = monthValues.FirstOrDefault(x => x.Date == date)?.BasicShiftCandidateCount ?? 0;
        BuildCells(monthValues, date);
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

public sealed record CalendarWorkSummaryRow(string DurationText, string AmountText);
