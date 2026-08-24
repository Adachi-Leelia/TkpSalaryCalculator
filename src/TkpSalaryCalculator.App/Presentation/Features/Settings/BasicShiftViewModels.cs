using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Features.Calendar;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.ValueObjects;
using TkpSalaryCalculator.Domain.Models;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

public sealed record WeekdayOption(DayOfWeek Value, string DisplayName);

public sealed record BasicShiftRow(
    BasicShiftId Id,
    string WorkText,
    string TimeText,
    string OrderText,
    string StatusText,
    string WarningText,
    Func<Task> Edit,
    Func<Task> Delete,
    Action<Exception> OnException)
{
    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningText);
    public AsyncCommand EditCommand { get; } = new(Edit, OnException);
    public AsyncCommand DeleteCommand { get; } = new(Delete, OnException);
}

public sealed record BasicShiftGroup(string WeekdayText, IReadOnlyList<BasicShiftRow> Rows)
{
    public bool HasRows => Rows.Count != 0;
    public bool HasNoRows => !HasRows;
}

/// <summary>SCR-SHIFT-01 の曜日別一覧、警告、編集および削除を管理します。</summary>
public sealed class BasicShiftViewModel : ViewModelBase
{
    private readonly IBasicShiftUseCase shifts;
    private readonly IWorkRecordUseCase workRecords;
    private readonly ISettingsNavigator navigator;
    private readonly IConfirmationDialogService dialogs;
    private readonly IUtcClock clock;
    private readonly ILocalDateConverter localDates;
    private readonly JapaneseDisplayFormatter formatter;
    private readonly IAppSessionState sessionState;
    private IReadOnlyList<BasicShiftGroup> groups = [];
    private string successMessage = string.Empty;

    public BasicShiftViewModel(
        IBasicShiftUseCase shifts,
        IWorkRecordUseCase workRecords,
        ISettingsNavigator navigator,
        IConfirmationDialogService dialogs,
        IUtcClock clock,
        ILocalDateConverter localDates,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter,
        IAppSessionState sessionState) : base(errorPresenter)
    {
        this.shifts = shifts ?? throw new ArgumentNullException(nameof(shifts));
        this.workRecords = workRecords ?? throw new ArgumentNullException(nameof(workRecords));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.localDates = localDates ?? throw new ArgumentNullException(nameof(localDates));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        this.sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        TrackDataChanges(this.sessionState, AppDataChangeKind.BasicShifts | AppDataChangeKind.Settings);
        AddCommand = new AsyncCommand(() => navigator.OpenBasicShiftEditorAsync(null, CancellationToken.None), PresentError);
    }

    public IReadOnlyList<BasicShiftGroup> Groups
    {
        get => groups;
        private set
        {
            if (!SetProperty(ref groups, value)) return;
            OnPropertyChanged(nameof(HasNoRows));
        }
    }
    public bool HasNoRows => Groups.All(x => !x.HasRows);
    public string SuccessMessage
    {
        get => successMessage;
        private set
        {
            if (!SetProperty(ref successMessage, value)) return;
            OnPropertyChanged(nameof(HasSuccessMessage));
        }
    }
    public bool HasSuccessMessage => !string.IsNullOrWhiteSpace(SuccessMessage);
    public AsyncCommand AddCommand { get; }
    public void SetSuccessMessage(string? value) => SuccessMessage = value ?? string.Empty;
    public Task LoadAsync() => LoadTrackedAsync(LoadCoreAsync, force: true);
    public Task LoadIfNeededAsync() => LoadTrackedAsync(LoadCoreAsync, force: false);

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var today = localDates.ToLocalDate(clock.UtcNow);
        var monthSettings = await workRecords.GetSettingsForDateAsync(today, cancellationToken);
        var services = monthSettings.Snapshot.Services.ToDictionary(x => x.Id, x => x.DisplayName);
        var categories = monthSettings.Snapshot.TimeCategories.ToDictionary(x => x.Id, x => x.DisplayName);
        var values = new List<BasicShiftGroup>();
        foreach (var weekday in OrderedWeekdays)
        {
            var weekdayShifts = await shifts.GetForWeekdayAsync(weekday, cancellationToken);
            var rows = weekdayShifts.Select(shift =>
            {
                var service = services.GetValueOrDefault(shift.ServiceId, "現在の設定にないサービス");
                var category = shift.TimeCategoryId is { } categoryId ? categories.GetValueOrDefault(categoryId, "現在の設定にない時間区分") : "任意時間";
                var time = shift.InputMode == WorkInputMode.TimeRange && shift.StartTime is { } start && shift.EndTime is { } end
                    ? $"{formatter.Time(start)}～{formatter.Time(end)} / {formatter.Duration(shift.WorkMinutes)}"
                    : formatter.Duration(shift.WorkMinutes);
                var warnings = new List<string>();
                if (!shift.IsEnabled) warnings.Add("この基本シフトは無効になっています。");
                if (!monthSettings.Snapshot.Services.Any(x => x.Id == shift.ServiceId && x.IsEnabled))
                    warnings.Add("現在の設定でサービスを利用できません。");
                if (shift.TimeCategoryId is { } timeCategoryId &&
                    !monthSettings.Snapshot.TimeCategories.Any(x => x.Id == timeCategoryId && x.ServiceId == shift.ServiceId && x.IsEnabled))
                    warnings.Add("現在の設定で時間区分を利用できません。");
                if (shift.InputMode == WorkInputMode.TimeRange && (shift.StartTime is null || shift.EndTime is null))
                    warnings.Add("開始時刻と終了時刻を設定してください。");
                return new BasicShiftRow(
                    shift.Id, $"{service} / {category}", time, $"表示順 {shift.DisplayOrder.Value}", shift.IsEnabled ? "使用中" : "無効",
                    string.Join(Environment.NewLine, warnings),
                    () => navigator.OpenBasicShiftEditorAsync(shift.Id.Value, CancellationToken.None),
                    () => DeleteAsync(shift), PresentError);
            }).ToArray();
            values.Add(new BasicShiftGroup(WeekdayName(weekday), rows));
        }
        Groups = values;
    }

    private Task DeleteAsync(BasicShiftDto shift) => RunBusyAsync(async cancellationToken =>
    {
        var confirmed = await dialogs.ConfirmAsync(
            "基本シフトを削除しますか",
            "基本シフトを削除します。すでに反映した勤務記録は変更・削除されません。",
            "削除", "キャンセル", cancellationToken);
        if (!confirmed) return;
        await shifts.DeleteAsync(shift.Id, cancellationToken);
        sessionState.NotifyDataChanged(AppDataChangeKind.BasicShifts | AppDataChangeKind.BackupStatus);
        var generation = CaptureTrackedDataGeneration();
        SuccessMessage = "基本シフトを削除しました。反映済みの勤務記録は維持されています。";
        await LoadCoreAsync(cancellationToken);
        AcceptDataGeneration(generation);
    });

    internal static DayOfWeek[] OrderedWeekdays { get; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
    internal static string WeekdayName(DayOfWeek value) => value switch
    {
        DayOfWeek.Monday => "月曜日", DayOfWeek.Tuesday => "火曜日", DayOfWeek.Wednesday => "水曜日",
        DayOfWeek.Thursday => "木曜日", DayOfWeek.Friday => "金曜日", DayOfWeek.Saturday => "土曜日", _ => "日曜日",
    };
}

/// <summary>SCR-SHIFT-02 の勤務内容、表示順、有効状態を編集します。</summary>
public sealed class BasicShiftEditorViewModel : EditableViewModelBase
{
    private readonly IBasicShiftUseCase shifts;
    private readonly IWorkRecordUseCase workRecords;
    private readonly ISettingsNavigator navigator;
    private readonly IUtcClock clock;
    private readonly ILocalDateConverter localDates;
    private readonly IAppSessionState sessionState;
    private readonly IssuePresenter issuePresenter;
    private BasicShiftId? id;
    private bool initializing;
    private IReadOnlyList<ServiceOptionViewModel> services = [];
    private IReadOnlyList<TimeCategoryOptionViewModel> timeCategories = [];
    private IReadOnlyList<SnapshotTimeCategory> allCategories = [];
    private IReadOnlyList<SnapshotPremium> premiums = [];
    private ServiceOptionViewModel? selectedService;
    private TimeCategoryOptionViewModel? selectedTimeCategory;
    private WeekdayOption selectedWeekday;
    private WorkInputModeOption selectedInputMode = WorkInputModeOption.Duration;
    private string workMinutesText = "60";
    private TimeSpan startTime = new(9, 0, 0);
    private TimeSpan endTime = new(10, 0, 0);
    private string displayOrderText = "0";
    private bool isEnabled = true;
    private IReadOnlyDictionary<string, string> fieldErrors = new Dictionary<string, string>();
    private string? firstInvalidField;

    public BasicShiftEditorViewModel(
        IBasicShiftUseCase shifts,
        IWorkRecordUseCase workRecords,
        ISettingsNavigator navigator,
        IUtcClock clock,
        ILocalDateConverter localDates,
        IUserErrorPresenter errorPresenter,
        IssuePresenter issuePresenter,
        IConfirmationDialogService dialogs,
        IAppSessionState sessionState) : base(errorPresenter, dialogs)
    {
        this.shifts = shifts ?? throw new ArgumentNullException(nameof(shifts));
        this.workRecords = workRecords ?? throw new ArgumentNullException(nameof(workRecords));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.localDates = localDates ?? throw new ArgumentNullException(nameof(localDates));
        this.sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        this.issuePresenter = issuePresenter ?? throw new ArgumentNullException(nameof(issuePresenter));
        TrackDataChanges(this.sessionState, AppDataChangeKind.BasicShifts | AppDataChangeKind.Settings);
        Weekdays = BasicShiftViewModel.OrderedWeekdays.Select(x => new WeekdayOption(x, BasicShiftViewModel.WeekdayName(x))).ToArray();
        selectedWeekday = Weekdays[0];
        InputModes = [WorkInputModeOption.Duration, WorkInputModeOption.TimeRange];
        SaveCommand = new AsyncCommand(SaveAsync, PresentError);
    }

    public string PageTitle => id is null ? "基本シフトを追加" : "基本シフトを編集";
    public IReadOnlyList<WeekdayOption> Weekdays { get; }
    public IReadOnlyList<WorkInputModeOption> InputModes { get; }
    public IReadOnlyList<ServiceOptionViewModel> Services { get => services; private set => SetProperty(ref services, value); }
    public IReadOnlyList<TimeCategoryOptionViewModel> TimeCategories { get => timeCategories; private set => SetProperty(ref timeCategories, value); }
    public WeekdayOption SelectedWeekday
    {
        get => selectedWeekday;
        set
        {
            if (!SetProperty(ref selectedWeekday, value)) return;
            OnPropertyChanged(nameof(ShowStartTime));
            Changed();
        }
    }
    public ServiceOptionViewModel? SelectedService
    {
        get => selectedService;
        set
        {
            if (!SetProperty(ref selectedService, value)) return;
            RebuildCategories(value?.Id);
            OnPropertyChanged(nameof(ShowStartTime));
            Changed();
        }
    }
    public TimeCategoryOptionViewModel? SelectedTimeCategory { get => selectedTimeCategory; set { if (SetProperty(ref selectedTimeCategory, value)) Changed(); } }
    public WorkInputModeOption SelectedInputMode
    {
        get => selectedInputMode;
        set
        {
            if (!SetProperty(ref selectedInputMode, value)) return;
            OnPropertyChanged(nameof(ShowDuration)); OnPropertyChanged(nameof(ShowTimeRange)); OnPropertyChanged(nameof(ShowStartTime)); Changed();
        }
    }
    public bool ShowDuration => SelectedInputMode.Value == WorkInputMode.Duration;
    public bool ShowTimeRange => SelectedInputMode.Value == WorkInputMode.TimeRange;
    public bool ShowStartTime => ShowTimeRange || SelectedService is { } service && HasApplicableTimedPremium(service.Id);
    public string WorkMinutesText { get => workMinutesText; set { if (SetProperty(ref workMinutesText, value)) Changed(); } }
    public TimeSpan StartTime { get => startTime; set { if (SetProperty(ref startTime, value)) Changed(); } }
    public TimeSpan EndTime { get => endTime; set { if (SetProperty(ref endTime, value)) Changed(); } }
    public string DisplayOrderText { get => displayOrderText; set { if (SetProperty(ref displayOrderText, value)) Changed(); } }
    public bool IsEnabled { get => isEnabled; set { if (SetProperty(ref isEnabled, value)) Changed(); } }
    public IReadOnlyDictionary<string, string> FieldErrors
    {
        get => fieldErrors;
        private set
        {
            if (!SetProperty(ref fieldErrors, value)) return;
            OnPropertyChanged(nameof(ServiceError));
            OnPropertyChanged(nameof(TimeCategoryError));
            OnPropertyChanged(nameof(WorkMinutesError));
            OnPropertyChanged(nameof(StartTimeError));
            OnPropertyChanged(nameof(EndTimeError));
            OnPropertyChanged(nameof(DisplayOrderError));
        }
    }
    public string ServiceError => FieldErrors.GetValueOrDefault("ServiceId", string.Empty);
    public string TimeCategoryError => FieldErrors.GetValueOrDefault("TimeCategoryId", string.Empty);
    public string WorkMinutesError => FieldErrors.GetValueOrDefault("WorkMinutes", string.Empty);
    public string StartTimeError => FieldErrors.GetValueOrDefault("StartTime", string.Empty);
    public string EndTimeError => FieldErrors.GetValueOrDefault("EndTime", string.Empty);
    public string DisplayOrderError => FieldErrors.GetValueOrDefault("DisplayOrder", string.Empty);
    public string? FirstInvalidField
    {
        get => firstInvalidField;
        private set => SetProperty(ref firstInvalidField, value);
    }
    public AsyncCommand SaveCommand { get; }

    public void Initialize(BasicShiftId? value) { id = value; InvalidateTrackedLoad(); OnPropertyChanged(nameof(PageTitle)); }

    public Task LoadAsync() => LoadTrackedAsync(LoadCoreAsync, force: true);
    public Task LoadIfNeededAsync() => LoadTrackedAsync(LoadCoreAsync, force: false);

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        initializing = true;
        try
        {
            var today = localDates.ToLocalDate(clock.UtcNow);
            var monthSettings = await workRecords.GetSettingsForDateAsync(today, cancellationToken);
            allCategories = monthSettings.Snapshot.TimeCategories;
            premiums = monthSettings.Snapshot.Premiums;
            Services = monthSettings.Snapshot.Services.OrderBy(x => x.DisplayOrder.Value)
                .Select(x => new ServiceOptionViewModel(x.Id, x.DisplayName, x.IsEnabled)).ToArray();
            BasicShiftDto? existing = null;
            if (id is { } shiftId)
            {
                foreach (var weekday in BasicShiftViewModel.OrderedWeekdays)
                {
                    existing = (await shifts.GetForWeekdayAsync(weekday, cancellationToken)).FirstOrDefault(x => x.Id == shiftId);
                    if (existing is not null) break;
                }
                if (existing is null) throw new ApplicationErrorException("SHIFT_NOT_FOUND", "編集する基本シフトが見つかりませんでした。");
            }
            Apply(existing);
        }
        finally { initializing = false; }
        ClearValidation();
        MarkSaved();
    }

    public Task SaveAsync() => RunBusyAsync(async cancellationToken =>
    {
        if (SelectedService is null) throw new ApplicationErrorException("SHIFT_SERVICE_REQUIRED", "サービスを選択してください。", "ServiceId");
        if (!int.TryParse(DisplayOrderText, out var order) || order < 0) throw new ApplicationErrorException("SHIFT_ORDER_INVALID", "表示順は0以上の整数で入力してください。", "DisplayOrder");
        WorkMinutes? minutes = null;
        MinuteOfDay? start = null;
        MinuteOfDay? end = null;
        if (SelectedInputMode.Value == WorkInputMode.Duration)
        {
            if (!int.TryParse(WorkMinutesText, out var parsed) || parsed is < 1 or > 1440)
                throw new ApplicationErrorException("SHIFT_DURATION_INVALID", "勤務時間は1分から1,440分で入力してください。", "WorkMinutes");
            minutes = new WorkMinutes(parsed);
            start = ShowStartTime ? new MinuteOfDay((int)StartTime.TotalMinutes) : null;
        }
        else
        {
            start = new MinuteOfDay((int)StartTime.TotalMinutes);
            end = new MinuteOfDay((int)EndTime.TotalMinutes);
        }
        await shifts.SaveAsync(new SaveBasicShiftCommand(
            id, SelectedWeekday.Value, null, SelectedService.Id, SelectedTimeCategory?.Id,
            SelectedInputMode.Value, minutes, start, end, new DisplayOrder(order), IsEnabled), cancellationToken);
        sessionState.NotifyDataChanged(AppDataChangeKind.BasicShifts | AppDataChangeKind.BackupStatus);
        MarkSaved();
        await navigator.GoBackAsync("基本シフトを保存しました。反映済みの勤務記録は変更されません。", cancellationToken);
    });

    private void Apply(BasicShiftDto? value)
    {
        selectedWeekday = value is null ? Weekdays[0] : Weekdays.First(x => x.Value == value.Weekday);
        OnPropertyChanged(nameof(SelectedWeekday));
        selectedService = value is null ? Services.FirstOrDefault(x => x.IsEnabled) : Services.FirstOrDefault(x => x.Id == value.ServiceId);
        OnPropertyChanged(nameof(SelectedService));
        RebuildCategories(selectedService?.Id);
        selectedTimeCategory = value is null ? TimeCategories[0] : TimeCategories.FirstOrDefault(x => x.Id == value.TimeCategoryId) ?? TimeCategories[0];
        OnPropertyChanged(nameof(SelectedTimeCategory));
        SelectedInputMode = value?.InputMode == WorkInputMode.TimeRange ? WorkInputModeOption.TimeRange : WorkInputModeOption.Duration;
        WorkMinutesText = (value?.WorkMinutes.Value ?? 60).ToString();
        if (value?.StartTime is { } start) StartTime = TimeSpan.FromMinutes(start.Value);
        if (value?.EndTime is { } end) EndTime = TimeSpan.FromMinutes(end.Value);
        DisplayOrderText = (value?.DisplayOrder.Value ?? 0).ToString();
        IsEnabled = value?.IsEnabled ?? true;
    }

    private void RebuildCategories(ServiceId? serviceId)
    {
        TimeCategories = new[] { TimeCategoryOptionViewModel.Arbitrary }.Concat(allCategories
            .Where(x => x.ServiceId == serviceId).OrderBy(x => x.DisplayOrder.Value)
            .Select(x => new TimeCategoryOptionViewModel(x.Id, x.DisplayName, x.StandardMinutes, x.IsEnabled))).ToArray();
        selectedTimeCategory = TimeCategories.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedTimeCategory));
    }

    private bool HasApplicableTimedPremium(ServiceId serviceId) => premiums.Any(x =>
        x.IsEnabled && x.StartTime is not null &&
        (x.ServiceIds.Count == 0 || x.ServiceIds.Contains(serviceId)) &&
        (x.Weekdays.Count == 0 || x.Weekdays.Contains(SelectedWeekday.Value)));

    protected override void OnErrorPresented(Exception exception)
    {
        if (exception is not ApplicationErrorException applicationError)
        {
            ClearValidation();
            return;
        }

        var presentation = issuePresenter.Present(
            [new IssueDto(applicationError.Code, applicationError.Field, applicationError.Message)]);
        FieldErrors = presentation.FieldErrors;
        FirstInvalidField = presentation.FirstInvalidField;
    }

    private void ClearValidation()
    {
        FieldErrors = new Dictionary<string, string>();
        FirstInvalidField = null;
    }

    private void Changed()
    {
        if (initializing) return;
        ClearError();
        ClearValidation();
        MarkDirty();
    }
}
