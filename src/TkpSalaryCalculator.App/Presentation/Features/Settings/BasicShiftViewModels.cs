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
                var warnings = new List<string>();
                if (!shift.IsEnabled) warnings.Add("この基本シフトは無効になっています。");
                foreach (var task in shift.Tasks.OrderBy(task => task.DisplayOrder.Value))
                {
                    if (!monthSettings.Snapshot.Services.Any(x => x.Id == task.ServiceId && x.IsEnabled))
                        warnings.Add($"タスク {task.DisplayOrder.Value + 1}: 現在の設定でサービスを利用できません。");
                    if (task.TimeCategoryId is { } categoryId &&
                        !monthSettings.Snapshot.TimeCategories.Any(x => x.Id == categoryId && x.ServiceId == task.ServiceId && x.IsEnabled))
                        warnings.Add($"タスク {task.DisplayOrder.Value + 1}: 現在の設定で時間区分を利用できません。");
                }
                var (name, time) = BasicShiftDisplay.Summarize(shift, services, categories, formatter);
                return new BasicShiftRow(
                    shift.Id, name, time, $"表示順 {shift.DisplayOrder.Value}", shift.IsEnabled ? "使用中" : "無効",
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

/// <summary>SCR-SHIFT-02 の全タスク、曜日、表示順、有効状態を編集します。</summary>
public sealed class BasicShiftEditorViewModel : EditableViewModelBase
{
    private readonly IBasicShiftUseCase shifts;
    private readonly IWorkRecordUseCase workRecords;
    private readonly ISettingsNavigator navigator;
    private readonly IUtcClock clock;
    private readonly ILocalDateConverter localDates;
    private readonly IAppSessionState sessionState;
    private BasicShiftId? id;
    private readonly IssuePresenter issuePresenter;
    private bool initializing;
    private bool hasSaved;
    private IReadOnlyList<ServiceOptionViewModel> services = [];
    private IReadOnlyList<SnapshotTimeCategory> categories = [];
    private IReadOnlyList<SnapshotPremium> premiums = [];
    private WeekdayOption selectedWeekday;
    private string displayOrderText = "0";
    private bool isEnabled = true;
    private string displayOrderError = string.Empty;
    private string? firstInvalidField;

    public BasicShiftEditorViewModel(
        IBasicShiftUseCase shifts, IWorkRecordUseCase workRecords, ISettingsNavigator navigator,
        IUtcClock clock, ILocalDateConverter localDates, IUserErrorPresenter errorPresenter,
        IssuePresenter issuePresenter, IConfirmationDialogService dialogs, IAppSessionState sessionState)
        : base(errorPresenter, dialogs)
    {
        this.shifts = shifts ?? throw new ArgumentNullException(nameof(shifts));
        this.workRecords = workRecords ?? throw new ArgumentNullException(nameof(workRecords));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.localDates = localDates ?? throw new ArgumentNullException(nameof(localDates));
        this.sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        this.issuePresenter = issuePresenter ?? throw new ArgumentNullException(nameof(issuePresenter));
        TrackDataChanges(sessionState, AppDataChangeKind.BasicShifts | AppDataChangeKind.Settings);
        Weekdays = BasicShiftViewModel.OrderedWeekdays.Select(x => new WeekdayOption(x, BasicShiftViewModel.WeekdayName(x))).ToArray();
        selectedWeekday = Weekdays[0];
        SaveCommand = new AsyncCommand(SaveAsync, PresentError, () => IsNotBusy && !hasSaved);
        AddTaskCommand = new AsyncCommand(AddTaskAsync, PresentError, () => IsNotBusy && !hasSaved);
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(IsBusy)) return;
            SaveCommand.NotifyCanExecuteChanged();
            AddTaskCommand.NotifyCanExecuteChanged();
        };
    }

    public string PageTitle => id is null ? "基本シフトを追加" : "基本シフトを編集";
    public IReadOnlyList<WeekdayOption> Weekdays { get; }
    public IReadOnlyList<WorkInputModeOption> InputModes { get; } = [WorkInputModeOption.Duration, WorkInputModeOption.TimeRange];
    public System.Collections.ObjectModel.ObservableCollection<WorkTaskEditorViewModel> Tasks { get; } = [];
    public string TaskCountText => $"タスク {Tasks.Count}件";
    public WeekdayOption SelectedWeekday
    {
        get => selectedWeekday;
        set
        {
            if (!SetProperty(ref selectedWeekday, value)) return;
            foreach (var task in Tasks) task.RefreshDateRules();
            Changed();
        }
    }
    public string DisplayOrderText { get => displayOrderText; set { if (SetProperty(ref displayOrderText, value)) Changed(); } }
    public bool IsEnabled { get => isEnabled; set { if (SetProperty(ref isEnabled, value)) Changed(); } }
    public string DisplayOrderError { get => displayOrderError; private set => SetProperty(ref displayOrderError, value); }
    public WorkTaskEditorViewModel? FirstInvalidTask { get; private set; }
    public string? FirstInvalidField { get => firstInvalidField; private set => SetProperty(ref firstInvalidField, value); }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand AddTaskCommand { get; }

    public void Initialize(BasicShiftId? value)
    {
        id = value;
        hasSaved = false;
        InvalidateTrackedLoad();
        OnPropertyChanged(nameof(PageTitle));
    }
    public Task LoadAsync() => LoadTrackedAsync(LoadCoreAsync, force: true);
    public Task LoadIfNeededAsync() => LoadTrackedAsync(LoadCoreAsync, force: false);

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        initializing = true;
        try
        {
            var today = localDates.ToLocalDate(clock.UtcNow);
            var monthSettings = await workRecords.GetSettingsForDateAsync(today, cancellationToken);
            categories = monthSettings.Snapshot.TimeCategories;
            premiums = monthSettings.Snapshot.Premiums;
            services = monthSettings.Snapshot.Services.OrderBy(x => x.DisplayOrder.Value)
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
            SelectedWeekday = existing is null ? Weekdays[0] : Weekdays.First(x => x.Value == existing.Weekday);
            DisplayOrderText = (existing?.DisplayOrder.Value ?? 0).ToString();
            IsEnabled = existing?.IsEnabled ?? true;
            Tasks.Clear();
            if (existing is null) Tasks.Add(CreateTask(null));
            else foreach (var task in existing.Tasks.OrderBy(x => x.DisplayOrder.Value)) Tasks.Add(CreateTask(task));
            UpdatePositions();
        }
        finally { initializing = false; }
        ClearValidation();
        MarkSaved();
    }

    private WorkTaskEditorViewModel CreateTask(BasicShiftTaskDto? task)
    {
        var taskId = new WorkTaskId(task?.Id.Value ?? Guid.NewGuid());
        var existing = task is null ? null : new WorkTaskDto(taskId, task.ServiceId, task.TimeCategoryId,
            task.InputMode, task.WorkMinutes, task.StartTime, task.EndTime, task.DisplayOrder, task.ServicePresetId);
        return new WorkTaskEditorViewModel(taskId, existing, [],
            services.Where(service => service.IsEnabled || service.Id == task?.ServiceId).ToArray(),
            categories, InputModes, HasApplicableTimedPremium, _ => Changed(),
            item => MoveTaskAsync(item, -1), item => MoveTaskAsync(item, 1), DeleteTaskAsync);
    }

    public Task AddTaskAsync()
    {
        if (IsBusy || hasSaved) return Task.CompletedTask;
        Tasks.Add(CreateTask(null));
        UpdatePositions();
        Changed();
        return Task.CompletedTask;
    }
    private Task MoveTaskAsync(WorkTaskEditorViewModel task, int offset)
    {
        var index = Tasks.IndexOf(task);
        if (IsBusy || hasSaved || index < 0 || index + offset < 0 || index + offset >= Tasks.Count) return Task.CompletedTask;
        Tasks.Move(index, index + offset);
        UpdatePositions();
        Changed();
        return Task.CompletedTask;
    }
    private Task DeleteTaskAsync(WorkTaskEditorViewModel task)
    {
        if (IsBusy || hasSaved || Tasks.Count <= 1 || !Tasks.Remove(task)) return Task.CompletedTask;
        UpdatePositions();
        Changed();
        return Task.CompletedTask;
    }
    private void UpdatePositions()
    {
        for (var index = 0; index < Tasks.Count; index++) Tasks[index].UpdatePosition(index, Tasks.Count);
        OnPropertyChanged(nameof(TaskCountText));
    }

    public Task SaveAsync()
    {
        if (hasSaved) return Task.CompletedTask;
        return RunBusyAsync(async cancellationToken =>
        {
            ClearValidation();
            if (!int.TryParse(DisplayOrderText, out var order) || order < 0)
                throw new ApplicationErrorException("SHIFT_ORDER_INVALID", "表示順は0以上の整数で入力してください。", "DisplayOrder");
            var values = new List<SaveBasicShiftTaskCommand>();
            foreach (var task in Tasks)
            {
                var field = $"Tasks[{task.Id.Value:D}]";
                if (task.SelectedService is null)
                    throw new ApplicationErrorException("SHIFT_SERVICE_REQUIRED", "サービスを選択してください。", $"{field}.ServiceId");
                WorkMinutes? minutes = null;
                if (task.ShowDuration)
                {
                    if (!int.TryParse(task.WorkMinutesText, out var parsed) || parsed is < 1 or > 1440)
                        throw new ApplicationErrorException("SHIFT_DURATION_INVALID", "勤務時間は1分から1,440分で入力してください。", $"{field}.WorkMinutes");
                    minutes = new WorkMinutes(parsed);
                }
                values.Add(new SaveBasicShiftTaskCommand(new BasicShiftTaskId(task.Id.Value),
                    task.SelectedPreset?.Id ?? task.OriginalSourceServicePresetId, task.SelectedService.Id,
                    task.SelectedTimeCategory?.Id, task.SelectedInputMode.Value, minutes,
                    task.ShowStartTime ? new MinuteOfDay((int)task.StartTime.TotalMinutes) : null,
                    task.ShowEndTime ? new MinuteOfDay((int)task.EndTime.TotalMinutes) : null,
                    new DisplayOrder(task.DisplayOrder)));
            }
            var saved = await shifts.SaveAsync(new SaveBasicShiftCommand(id, SelectedWeekday.Value,
                values, new DisplayOrder(order), IsEnabled), cancellationToken);
            id = saved.Id;
            hasSaved = true;
            SaveCommand.NotifyCanExecuteChanged();
            AddTaskCommand.NotifyCanExecuteChanged();
            sessionState.NotifyDataChanged(AppDataChangeKind.BasicShifts | AppDataChangeKind.BackupStatus);
            MarkSaved();
            await navigator.GoBackAsync("基本シフトを保存しました。反映済みの勤務記録は変更されません。", cancellationToken);
        });
    }

    private bool HasApplicableTimedPremium(ServiceId serviceId) => premiums.Any(premium =>
    {
        if (!premium.IsEnabled || premium.StartTime is null ||
            (premium.ServiceIds.Count != 0 && !premium.ServiceIds.Contains(serviceId))) return false;

        var hasDateCondition = premium.Weekdays.Count != 0 || premium.UsesNationalHolidays || premium.Dates.Count != 0;
        return !hasDateCondition ||
            premium.Weekdays.Contains(SelectedWeekday.Value) ||
            premium.UsesNationalHolidays ||
            premium.Dates.Any(date => date.DayOfWeek == SelectedWeekday.Value);
    });

    protected override void OnErrorPresented(Exception exception)
    {
        if (exception is not ApplicationErrorException error) return;
        var presentation = issuePresenter.Present([new IssueDto(error.Code, error.Field, error.Message)]);
        DisplayOrderError = presentation.FieldErrors.GetValueOrDefault("DisplayOrder", string.Empty);
        foreach (var task in Tasks)
        {
            var prefix = $"Tasks[{task.Id.Value:D}].";
            if (error.Field?.StartsWith(prefix, StringComparison.Ordinal) != true) continue;
            var field = error.Field[prefix.Length..];
            task.AddError(field, error.Message);
            FirstInvalidTask = task;
            FirstInvalidField = field;
            return;
        }
        FirstInvalidField = presentation.FirstInvalidField;
    }
    private void ClearValidation()
    {
        DisplayOrderError = string.Empty;
        foreach (var task in Tasks) task.SetErrors([]);
        FirstInvalidTask = null;
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
