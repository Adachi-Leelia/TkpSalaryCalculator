using System.Collections.ObjectModel;
using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Calendar;

/// <summary>SCR-WORK-01 の訪問、複数タスク、プレビュー、保存、および項目エラーを管理します。</summary>
public sealed class WorkEditorViewModel : EditableViewModelBase
{
    private readonly IWorkRecordUseCase workRecords;
    private readonly ICalendarNavigator navigator;
    private readonly IssuePresenter issuePresenter;
    private readonly JapaneseDisplayFormatter formatter;
    private readonly IAppSessionState sessionState;
    private bool isInitializing;
    private bool hasSaved;
    private Guid operationId;
    private WorkRecordId? workRecordId;
    private DateOnly initialWorkDate;
    private DateOnly optionsDate;
    private DateTime workDate;
    private IReadOnlyList<PresetOptionViewModel> presetCandidates = [];
    private IReadOnlyList<ServiceOptionViewModel> services = [];
    private IReadOnlyList<SnapshotTimeCategory> allTimeCategories = [];
    private IReadOnlyList<SnapshotPremium> premiums = [];
    private HolidayCalendar? holidayCalendar;
    private string previewText = "入力後に訪問全体の給与をプレビューできます。";
    private string countBonusText = string.Empty;
    private string visitTotalText = string.Empty;
    private string issueMessage = string.Empty;
    private bool canSave;
    private string unavailableCandidatesText = string.Empty;
    private string? firstInvalidField;
    private WorkTaskEditorViewModel? firstInvalidTask;
    private WorkEditorScreenDto? editorScreen;
    private SaveWorkRecordCommand? previewedCommand;

    public WorkEditorViewModel(
        IWorkRecordUseCase workRecords,
        ICalendarNavigator navigator,
        IssuePresenter issuePresenter,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter,
        IConfirmationDialogService dialogs,
        IAppSessionState sessionState) : base(errorPresenter, dialogs)
    {
        this.workRecords = workRecords ?? throw new ArgumentNullException(nameof(workRecords));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.issuePresenter = issuePresenter ?? throw new ArgumentNullException(nameof(issuePresenter));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        this.sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        TrackDataChanges(this.sessionState, AppDataChangeKind.WorkRecords | AppDataChangeKind.Settings);
        InputModes = [WorkInputModeOption.Duration, WorkInputModeOption.TimeRange];
        ReloadCommand = new AsyncCommand(LoadAsync, PresentError);
        PreviewCommand = new AsyncCommand(PreviewAsync, PresentError, CanEditTasks);
        SaveCommand = new AsyncCommand(SaveAsync, PresentError,
            () => !hasSaved && IsNotBusy);
        AddTaskCommand = new AsyncCommand(AddTaskAsync, PresentError, CanEditTasks);
    }

    public bool IsEditing => WorkRecordId is not null;
    public string PageTitle => IsEditing ? "訪問を編集" : "訪問を追加";
    public WorkRecordId? WorkRecordId
    {
        get => workRecordId;
        private set
        {
            if (!SetProperty(ref workRecordId, value)) return;
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(PageTitle));
        }
    }

    public DateTime WorkDate
    {
        get => workDate;
        set
        {
            if (!SetProperty(ref workDate, value.Date)) return;
            OnPropertyChanged(nameof(SettingsMonthText));
            OnPropertyChanged(nameof(SettingsMonthChangeWarningText));
            OnPropertyChanged(nameof(HasSettingsMonthChangeWarning));
            foreach (var task in Tasks) task.RefreshDateRules();
            InputChanged();
        }
    }

    public string SettingsMonthText => $"適用する設定対象年月: {WorkDate:yyyy年M月}（全タスク共通）";
    public string SettingsMonthChangeWarningText => HasSettingsMonthChangeWarning
        ? $"勤務日を別の年月へ変更したため、全タスクには{WorkDate:yyyy年M月}の設定が適用されます。保存前にサービス、時間区分、単価、割増および件数加算を確認してください。"
        : string.Empty;
    public bool HasSettingsMonthChangeWarning =>
        WorkDate.Year != initialWorkDate.Year || WorkDate.Month != initialWorkDate.Month;
    public ObservableCollection<WorkTaskEditorViewModel> Tasks { get; } = [];
    public bool HasMultipleTasks => Tasks.Count > 1;
    public string TaskCountText => $"タスク {Tasks.Count}件";
    public IReadOnlyList<PresetOptionViewModel> PresetCandidates => presetCandidates;
    public IReadOnlyList<ServiceOptionViewModel> Services => services;
    public IReadOnlyList<WorkInputModeOption> InputModes { get; }
    public AsyncCommand ReloadCommand { get; }
    public AsyncCommand PreviewCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand AddTaskCommand { get; }

    public string PreviewText { get => previewText; private set => SetProperty(ref previewText, value); }
    public string CountBonusText
    {
        get => countBonusText;
        private set
        {
            if (!SetProperty(ref countBonusText, value)) return;
            OnPropertyChanged(nameof(HasCountBonus));
        }
    }
    public bool HasCountBonus => !string.IsNullOrWhiteSpace(CountBonusText);
    public string VisitTotalText
    {
        get => visitTotalText;
        private set
        {
            if (!SetProperty(ref visitTotalText, value)) return;
            OnPropertyChanged(nameof(HasVisitTotal));
        }
    }
    public bool HasVisitTotal => !string.IsNullOrWhiteSpace(VisitTotalText);
    public string IssueMessage
    {
        get => issueMessage;
        private set
        {
            if (!SetProperty(ref issueMessage, value)) return;
            OnPropertyChanged(nameof(HasIssues));
        }
    }
    public bool HasIssues => !string.IsNullOrWhiteSpace(IssueMessage) || Tasks.Any(task => task.HasErrors);
    public bool CanSave
    {
        get => canSave;
        private set
        {
            if (!SetProperty(ref canSave, value)) return;
            SaveCommand.NotifyCanExecuteChanged();
        }
    }
    public string UnavailableCandidatesText
    {
        get => unavailableCandidatesText;
        private set
        {
            if (!SetProperty(ref unavailableCandidatesText, value)) return;
            OnPropertyChanged(nameof(HasUnavailableCandidates));
        }
    }
    public bool HasUnavailableCandidates => !string.IsNullOrWhiteSpace(UnavailableCandidatesText);
    public string? FirstInvalidField
    {
        get => firstInvalidField;
        private set => SetProperty(ref firstInvalidField, value);
    }
    public WorkTaskEditorViewModel? FirstInvalidTask
    {
        get => firstInvalidTask;
        private set => SetProperty(ref firstInvalidTask, value);
    }

    public void Initialize(DateOnly date, WorkRecordId? id)
    {
        InvalidateTrackedLoad();
        operationId = Guid.NewGuid();
        hasSaved = false;
        editorScreen = null;
        previewedCommand = null;
        ResetScreenState();
        WorkRecordId = id;
        initialWorkDate = date;
        workDate = date.ToDateTime(TimeOnly.MinValue);
        OnPropertyChanged(nameof(WorkDate));
        OnPropertyChanged(nameof(SettingsMonthText));
        OnPropertyChanged(nameof(SettingsMonthChangeWarningText));
        OnPropertyChanged(nameof(HasSettingsMonthChangeWarning));
        AddTaskCore(null);
        MarkSaved();
        NotifyCommands();
    }

    public async Task LoadAsync()
    {
        await LoadTrackedAsync(LoadCoreAsync, force: true);
        NotifyCommands();
    }

    public async Task LoadIfNeededAsync()
    {
        await LoadTrackedAsync(LoadCoreAsync, force: false);
        NotifyCommands();
    }

    public async Task PreviewAsync()
    {
        await RunBusyAsync(PreviewCoreAsync);
        NotifyCommands();
    }

    public async Task SaveAsync()
    {
        await RunBusyAsync(async cancellationToken =>
        {
            var command = BuildCommand(out var localIssues);
            if (command is null)
            {
                PresentIssues(localIssues);
                return;
            }

            if (!CanSave || previewedCommand != command)
            {
                var preview = await PreviewCoreAsync(cancellationToken);
                if (preview is null || !preview.CanSave) return;
            }

            var saveTask = workRecords.SaveAsync(
                command,
                command.Id is null ? CancellationToken.None : cancellationToken);
            var result = await NotifyWhenSaveCompletesAsync(saveTask).WaitAsync(cancellationToken);
            ApplySavedNormalization(result.WorkRecord);
            hasSaved = true;
            MarkSaved();
            await navigator.GoBackAsync("勤務記録を保存しました。", cancellationToken);
        });
        NotifyCommands();
    }

    public Task AddTaskAsync()
    {
        if (!CanEditTasks()) return Task.CompletedTask;
        AddTaskCore(null);
        InputChanged();
        return Task.CompletedTask;
    }

    public Task MoveTaskUpAsync(WorkTaskEditorViewModel task) => MoveTaskAsync(task, -1);
    public Task MoveTaskDownAsync(WorkTaskEditorViewModel task) => MoveTaskAsync(task, 1);

    public Task DeleteTaskAsync(WorkTaskEditorViewModel task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!CanEditTasks()) return Task.CompletedTask;
        if (Tasks.Count <= 1 || !Tasks.Remove(task)) return Task.CompletedTask;
        UpdateTaskPositions();
        InputChanged();
        return Task.CompletedTask;
    }

    private Task MoveTaskAsync(WorkTaskEditorViewModel task, int offset)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!CanEditTasks()) return Task.CompletedTask;
        var oldIndex = Tasks.IndexOf(task);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Tasks.Count) return Task.CompletedTask;
        Tasks.Move(oldIndex, newIndex);
        UpdateTaskPositions();
        InputChanged();
        return Task.CompletedTask;
    }

    private async Task<SaveWorkRecordResultDto> NotifyWhenSaveCompletesAsync(Task<SaveWorkRecordResultDto> saveTask)
    {
        var result = await saveTask.ConfigureAwait(false);
        sessionState.NotifyDataChanged(AppDataChangeKind.WorkRecords | AppDataChangeKind.BackupStatus);
        return result;
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        isInitializing = true;
        try
        {
            var date = DateOnly.FromDateTime(WorkDate);
            editorScreen = await workRecords.GetEditorScreenAsync(date, WorkRecordId, cancellationToken);
            var existing = editorScreen.ExistingRecord;
            if (WorkRecordId is not null && existing is null)
                throw new InvalidOperationException("編集する訪問が見つかりませんでした。");

            PopulateOptions(editorScreen.InputOptions, editorScreen.HolidayCalendar, existing);
            Tasks.Clear();
            if (existing is null)
                AddTaskCore(null);
            else
                foreach (var task in existing.Tasks.OrderBy(task => task.DisplayOrder.Value)) AddTaskCore(task);
            UpdateTaskPositions();
        }
        finally
        {
            isInitializing = false;
        }

        if (editorScreen?.ExistingRecord is not null)
            await PreviewCoreAsync(cancellationToken);
        MarkSaved();
    }

    private void ResetScreenState()
    {
        isInitializing = true;
        try
        {
            Tasks.Clear();
            presetCandidates = [];
            services = [];
            allTimeCategories = [];
            premiums = [];
            holidayCalendar = null;
            UnavailableCandidatesText = string.Empty;
            PreviewText = "入力後に訪問全体の給与をプレビューできます。";
            CountBonusText = string.Empty;
            VisitTotalText = string.Empty;
            IssueMessage = string.Empty;
            FirstInvalidTask = null;
            FirstInvalidField = null;
            CanSave = false;
        }
        finally
        {
            isInitializing = false;
        }
        OnPropertyChanged(nameof(PresetCandidates));
        OnPropertyChanged(nameof(Services));
    }

    private async Task<WorkRecordPreviewDto?> PreviewCoreAsync(CancellationToken cancellationToken)
    {
        await EnsureOptionsForSelectedDateAsync(cancellationToken);
        var command = BuildCommand(out var localIssues);
        if (command is null)
        {
            PresentIssues(localIssues);
            CanSave = false;
            return null;
        }

        var screen = editorScreen ?? throw new InvalidOperationException("勤務入力画面のデータを読み込んでください。");
        var preview = await workRecords.PreviewForEditorAsync(command, screen, cancellationToken);
        previewedCommand = preview.CanSave ? command : null;
        ApplyPreview(preview);
        return preview;
    }

    private async Task EnsureOptionsForSelectedDateAsync(CancellationToken cancellationToken)
    {
        var selectedDate = DateOnly.FromDateTime(WorkDate);
        if (selectedDate == optionsDate) return;
        isInitializing = true;
        try
        {
            editorScreen = await workRecords.GetEditorScreenAsync(selectedDate, WorkRecordId, cancellationToken);
            PopulateOptions(editorScreen.InputOptions, editorScreen.HolidayCalendar, editorScreen.ExistingRecord);
            foreach (var task in Tasks)
                task.UpdateOptions(presetCandidates, services, allTimeCategories);
        }
        finally
        {
            isInitializing = false;
        }
    }

    private void PopulateOptions(WorkInputOptionsDto options, HolidayCalendar calendar, WorkRecordDto? existing)
    {
        optionsDate = options.WorkDate;
        holidayCalendar = calendar;
        var originalServiceIds = existing?.Tasks.Select(task => task.ServiceId).ToHashSet() ?? [];
        presetCandidates = options.PresetCandidates
            .Where(candidate => candidate.IsAvailable)
            .OrderBy(candidate => candidate.Preset.DisplayOrder.Value)
            .ThenBy(candidate => candidate.Preset.DisplayName, StringComparer.CurrentCulture)
            .Select(candidate => new PresetOptionViewModel(
                candidate.Preset.Id,
                candidate.Preset.DisplayName,
                candidate.Preset.ServiceId,
                candidate.Preset.TimeCategoryId,
                candidate.Preset.DefaultWorkMinutes,
                candidate.IsAvailable,
                string.Join(Environment.NewLine, candidate.Issues.Select(issue => issue.Message))))
            .ToArray();
        services = options.Settings.Snapshot.Services
            .Where(service => service.IsEnabled || originalServiceIds.Contains(service.Id))
            .OrderBy(service => service.DisplayOrder.Value)
            .ThenBy(service => service.DisplayName, StringComparer.CurrentCulture)
            .Select(service => new ServiceOptionViewModel(service.Id, service.DisplayName, service.IsEnabled))
            .ToArray();
        allTimeCategories = options.Settings.Snapshot.TimeCategories;
        premiums = options.Settings.Snapshot.Premiums;
        UnavailableCandidatesText = string.Join(Environment.NewLine,
            options.PresetCandidates.Where(candidate => !candidate.IsAvailable).Select(FormatUnavailableCandidate));
        OnPropertyChanged(nameof(PresetCandidates));
        OnPropertyChanged(nameof(Services));
    }

    private static string FormatUnavailableCandidate(ServicePresetCandidateDto value)
    {
        var reason = value.Issues.Count == 0
            ? "この候補は現在無効です。設定で有効にしてください。"
            : string.Join(Environment.NewLine, value.Issues.Select(issue => issue.Message));
        return $"利用できない候補: {value.Preset.DisplayName}: {reason}";
    }

    private void AddTaskCore(WorkTaskDto? existing)
    {
        var id = existing?.Id ?? (Tasks.Count == 0
            ? new WorkTaskId(operationId)
            : new WorkTaskId(Guid.NewGuid()));
        var task = new WorkTaskEditorViewModel(
            id,
            existing,
            presetCandidates,
            services,
            allTimeCategories,
            InputModes,
            HasApplicableTimedPremium,
            OnTaskInputChanged,
            MoveTaskUpAsync,
            MoveTaskDownAsync,
            DeleteTaskAsync);
        Tasks.Add(task);
        UpdateTaskPositions();
    }

    private void OnTaskInputChanged(WorkTaskEditorViewModel _)
    {
        if (isInitializing) return;
        InputChanged();
    }

    private void UpdateTaskPositions()
    {
        for (var index = 0; index < Tasks.Count; index++)
            Tasks[index].UpdatePosition(index, Tasks.Count);
        OnPropertyChanged(nameof(HasMultipleTasks));
        OnPropertyChanged(nameof(TaskCountText));
        OnPropertyChanged(nameof(HasIssues));
    }

    private bool HasApplicableTimedPremium(ServiceId serviceId)
    {
        var date = DateOnly.FromDateTime(WorkDate);
        return premiums.Any(premium =>
        {
            if (!premium.IsEnabled || premium.StartTime is null ||
                (premium.ServiceIds.Count != 0 && !premium.ServiceIds.Contains(serviceId))) return false;
            var hasDateCondition = premium.Weekdays.Count != 0 || premium.Dates.Count != 0 || premium.UsesNationalHolidays;
            return !hasDateCondition || premium.Weekdays.Contains(date.DayOfWeek) || premium.Dates.Contains(date) ||
                (premium.UsesNationalHolidays && holidayCalendar?.Holidays.ContainsKey(date) == true);
        });
    }

    private SaveWorkRecordCommand? BuildCommand(out IReadOnlyList<IssueDto> issues)
    {
        var local = new List<IssueDto>();
        var commands = new List<SaveWorkTaskCommand>(Tasks.Count);
        foreach (var task in Tasks)
        {
            var prefix = $"Tasks[{task.Id.Value:D}]";
            if (task.SelectedService is null)
                local.Add(new IssueDto("WORK_SERVICE_REQUIRED", $"{prefix}.ServiceId", "サービスを選択してください。"));

            WorkMinutes? minutes = null;
            if (task.SelectedInputMode.Value == WorkInputMode.Duration)
            {
                if (!int.TryParse(task.WorkMinutesText, out var value) || value is < 1 or > 1440)
                    local.Add(new IssueDto("WORK_MINUTES_OUT_OF_RANGE", $"{prefix}.WorkMinutes",
                        "勤務時間は1分以上24時間以内で入力してください。"));
                else
                    minutes = new WorkMinutes(value);
            }

            if (task.SelectedService is null) continue;
            var mode = task.SelectedInputMode.Value;
            commands.Add(new SaveWorkTaskCommand(
                task.Id,
                task.SelectedService.Id,
                task.SelectedTimeCategory?.Id,
                mode,
                minutes,
                mode == WorkInputMode.TimeRange || task.ShowStartTime ? ToMinuteOfDay(task.StartTime) : null,
                mode == WorkInputMode.TimeRange ? ToMinuteOfDay(task.EndTime) : null,
                new DisplayOrder(task.DisplayOrder),
                task.SelectedPreset?.Id ?? task.OriginalSourceServicePresetId));
        }

        if (local.Count != 0)
        {
            issues = local;
            return null;
        }

        issues = [];
        return new SaveWorkRecordCommand(
            WorkRecordId,
            DateOnly.FromDateTime(WorkDate),
            commands,
            WorkRecordId is null ? operationId : null);
    }

    private void ApplyPreview(WorkRecordPreviewDto preview)
    {
        PresentIssues(preview.Issues);
        CanSave = preview.CanSave;
        var normalizedById = preview.Tasks
            .Where(task => task.WorkTaskId.Value != Guid.Empty)
            .ToDictionary(task => task.WorkTaskId);
        var calculationById = preview.Calculation?.TaskCalculations.ToDictionary(task => task.WorkTaskId) ?? [];
        for (var index = 0; index < Tasks.Count; index++)
        {
            var task = Tasks[index];
            WorkTaskPreviewDto? normalized = normalizedById.GetValueOrDefault(task.Id);
            TaskSalaryCalculation? calculation = calculationById.GetValueOrDefault(task.Id);
            task.ApplyPreview(normalized, calculation, formatter);
        }

        if (preview.Calculation?.Status == SalaryCalculationStatus.Calculated && preview.Calculation.Total is { } total)
        {
            CountBonusText = preview.Calculation.CountBonuses.Count == 0
                ? "訪問の件数加算: なし"
                : "訪問の件数加算: " + string.Join("、", preview.Calculation.CountBonuses
                    .Select(bonus => $"{bonus.DisplayName} {formatter.Money(bonus.Amount)}"));
            VisitTotalText = $"訪問合計 {formatter.Money(total)}";
            PreviewText = "全タスクを含む訪問の給与見込みです。";
        }
        else if (preview.Calculation?.Status == SalaryCalculationStatus.Uncalculated)
        {
            CountBonusText = string.Empty;
            VisitTotalText = string.Empty;
            PreviewText = "未計算（勤務内容は保存できます）。不足しているタスクを確認してください。";
        }
        else
        {
            CountBonusText = string.Empty;
            VisitTotalText = string.Empty;
            PreviewText = "入力を修正すると訪問全体の給与をプレビューできます。";
        }
    }

    private void ApplySavedNormalization(WorkRecordDto record)
    {
        var byId = record.Tasks.ToDictionary(task => task.Id);
        foreach (var task in Tasks)
        {
            if (byId.TryGetValue(task.Id, out var saved)) task.ApplyNormalized(saved, formatter);
        }
    }

    private void PresentIssues(IReadOnlyList<IssueDto> issues)
    {
        foreach (var task in Tasks) task.SetErrors([]);
        FirstInvalidTask = null;
        FirstInvalidField = null;

        var screenIssues = new List<IssueDto>();
        foreach (var issue in issues)
        {
            if (TryParseTaskField(issue.Field, out var taskId, out var field) &&
                Tasks.FirstOrDefault(task => task.Id == taskId) is { } task)
            {
                task.AddError(field, issue.Message);
                if (FirstInvalidTask is null)
                {
                    FirstInvalidTask = task;
                    FirstInvalidField = field;
                }
            }
            else
            {
                screenIssues.Add(issue with { Field = null });
            }
        }

        IssueMessage = issuePresenter.Present(screenIssues).ScreenMessage ?? string.Empty;
        OnPropertyChanged(nameof(HasIssues));
    }

    private static bool TryParseTaskField(string? value, out WorkTaskId taskId, out string field)
    {
        taskId = default;
        field = string.Empty;
        const string prefix = "Tasks[";
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var close = value.IndexOf(']', prefix.Length);
        if (close < 0 || !Guid.TryParse(value[prefix.Length..close], out var id)) return false;
        field = close + 1 < value.Length && value[close + 1] == '.' ? value[(close + 2)..] : "Tasks";
        taskId = new WorkTaskId(id);
        return true;
    }

    private void InputChanged()
    {
        if (isInitializing) return;
        previewedCommand = null;
        MarkDirty();
        CanSave = false;
        PreviewText = "入力内容が変わりました。訪問全体を再プレビューしてください。";
        CountBonusText = string.Empty;
        VisitTotalText = string.Empty;
        foreach (var task in Tasks)
        {
            task.SetErrors([]);
            task.ResetPreview();
        }
        IssueMessage = string.Empty;
        FirstInvalidTask = null;
        FirstInvalidField = null;
        OnPropertyChanged(nameof(HasIssues));
        PreviewCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanEditTasks() => !hasSaved && IsNotBusy;

    private void NotifyCommands()
    {
        PreviewCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        foreach (var task in Tasks) task.NotifyCommands();
    }

    private static MinuteOfDay ToMinuteOfDay(TimeSpan value) => new((int)value.TotalMinutes);
}

/// <summary>勤務入力画面の1タスクカードに閉じた編集状態を保持します。</summary>
public sealed class WorkTaskEditorViewModel : ObservableObject
{
    private readonly IReadOnlyList<WorkInputModeOption> inputModes;
    private readonly Func<ServiceId, bool> hasApplicableTimedPremium;
    private readonly Action<WorkTaskEditorViewModel> inputChanged;
    private bool suppressChanges;
    private IReadOnlyList<PresetOptionViewModel> presetCandidates;
    private IReadOnlyList<ServiceOptionViewModel> services;
    private IReadOnlyList<SnapshotTimeCategory> allTimeCategories;
    private IReadOnlyList<TimeCategoryOptionViewModel> timeCategories = [];
    private PresetOptionViewModel? selectedPreset;
    private ServiceOptionViewModel? selectedService;
    private TimeCategoryOptionViewModel? selectedTimeCategory;
    private WorkInputModeOption selectedInputMode;
    private string workMinutesText = string.Empty;
    private TimeSpan startTime = new(9, 0, 0);
    private TimeSpan endTime = new(10, 0, 0);
    private int displayOrder;
    private int taskCount = 1;
    private string previewText = "入力後にタスク給与を表示します。";
    private string normalizedTimeText = string.Empty;
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    public WorkTaskEditorViewModel(
        WorkTaskId id,
        WorkTaskDto? existing,
        IReadOnlyList<PresetOptionViewModel> presetCandidates,
        IReadOnlyList<ServiceOptionViewModel> services,
        IReadOnlyList<SnapshotTimeCategory> timeCategories,
        IReadOnlyList<WorkInputModeOption> inputModes,
        Func<ServiceId, bool> hasApplicableTimedPremium,
        Action<WorkTaskEditorViewModel> inputChanged,
        Func<WorkTaskEditorViewModel, Task> moveUp,
        Func<WorkTaskEditorViewModel, Task> moveDown,
        Func<WorkTaskEditorViewModel, Task> delete)
    {
        Id = id;
        this.presetCandidates = presetCandidates;
        this.services = services;
        allTimeCategories = timeCategories;
        this.inputModes = inputModes;
        this.hasApplicableTimedPremium = hasApplicableTimedPremium;
        this.inputChanged = inputChanged;
        selectedInputMode = inputModes[0];
        OriginalServiceId = existing?.ServiceId;
        OriginalTimeCategoryId = existing?.TimeCategoryId;
        OriginalSourceServicePresetId = existing?.SourceServicePresetId;
        MoveUpCommand = new AsyncCommand(() => moveUp(this), _ => { }, () => CanMoveUp);
        MoveDownCommand = new AsyncCommand(() => moveDown(this), _ => { }, () => CanMoveDown);
        DeleteCommand = new AsyncCommand(() => delete(this), _ => { }, () => CanRemove);

        suppressChanges = true;
        try
        {
            if (existing is not null)
            {
                selectedService = services.FirstOrDefault(service => service.Id == existing.ServiceId);
                RebuildTimeCategories(existing.TimeCategoryId);
                selectedTimeCategory = FindTimeCategory(existing.TimeCategoryId);
                selectedInputMode = inputModes.First(mode => mode.Value == existing.InputMode);
                workMinutesText = existing.WorkMinutes.Value.ToString();
                if (existing.StartTime is { } start) startTime = TimeSpan.FromMinutes(start.Value);
                if (existing.EndTime is { } end) endTime = TimeSpan.FromMinutes(end.Value);
                selectedPreset = presetCandidates.FirstOrDefault(preset => preset.Id == existing.SourceServicePresetId);
            }
            else
            {
                RebuildTimeCategories(null);
            }
        }
        finally
        {
            suppressChanges = false;
        }
    }

    public WorkTaskId Id { get; }
    public ServiceId? OriginalServiceId { get; }
    public TimeCategoryId? OriginalTimeCategoryId { get; }
    public ServicePresetId? OriginalSourceServicePresetId { get; }
    public IReadOnlyList<PresetOptionViewModel> PresetCandidates => presetCandidates;
    public IReadOnlyList<ServiceOptionViewModel> Services => services;
    public IReadOnlyList<TimeCategoryOptionViewModel> TimeCategories => timeCategories;
    public IReadOnlyList<WorkInputModeOption> InputModes => inputModes;
    public int DisplayOrder => displayOrder;
    public string TaskTitle => $"タスク {DisplayOrder + 1}";
    public string AccessibilityText => $"訪問内の{TaskTitle}、全{taskCount}件";
    public bool CanMoveUp => DisplayOrder > 0;
    public bool CanMoveDown => DisplayOrder < taskCount - 1;
    public bool CanRemove => taskCount > 1;
    public string DeleteAccessibilityText => CanRemove
        ? $"{TaskTitle}を削除"
        : "最後の1タスクは削除できません";
    public string ServiceAutomationId => $"Task-{Id.Value:D}-ServiceId";
    public string TimeCategoryAutomationId => $"Task-{Id.Value:D}-TimeCategoryId";
    public string WorkMinutesAutomationId => $"Task-{Id.Value:D}-WorkMinutes";
    public string StartTimeAutomationId => $"Task-{Id.Value:D}-StartTime";
    public string EndTimeAutomationId => $"Task-{Id.Value:D}-EndTime";
    public AsyncCommand MoveUpCommand { get; }
    public AsyncCommand MoveDownCommand { get; }
    public AsyncCommand DeleteCommand { get; }

    public PresetOptionViewModel? SelectedPreset
    {
        get => selectedPreset;
        set
        {
            if (!SetProperty(ref selectedPreset, value)) return;
            if (value is not null) ApplyPreset(value);
            Changed();
        }
    }
    public ServiceOptionViewModel? SelectedService
    {
        get => selectedService;
        set
        {
            if (!SetProperty(ref selectedService, value)) return;
            RebuildTimeCategories(SelectedTimeCategory?.Id);
            OnPropertyChanged(nameof(ShowStartTime));
            Changed();
        }
    }
    public TimeCategoryOptionViewModel? SelectedTimeCategory
    {
        get => selectedTimeCategory;
        set
        {
            if (!SetProperty(ref selectedTimeCategory, value)) return;
            if (value?.StandardMinutes is { } standardMinutes) WorkMinutesText = standardMinutes.Value.ToString();
            Changed();
        }
    }
    public WorkInputModeOption SelectedInputMode
    {
        get => selectedInputMode;
        set
        {
            if (!SetProperty(ref selectedInputMode, value)) return;
            OnPropertyChanged(nameof(ShowDuration));
            OnPropertyChanged(nameof(ShowStartTime));
            OnPropertyChanged(nameof(ShowEndTime));
            Changed();
        }
    }
    public bool HasTimeCategories => TimeCategories.Count > 1;
    public bool ShowDuration => SelectedInputMode.Value == WorkInputMode.Duration;
    public bool ShowEndTime => SelectedInputMode.Value == WorkInputMode.TimeRange;
    public bool ShowStartTime => ShowEndTime || SelectedService is { } service && hasApplicableTimedPremium(service.Id);
    public string WorkMinutesText
    {
        get => workMinutesText;
        set { if (SetProperty(ref workMinutesText, value)) Changed(); }
    }
    public TimeSpan StartTime
    {
        get => startTime;
        set { if (SetProperty(ref startTime, value)) Changed(); }
    }
    public TimeSpan EndTime
    {
        get => endTime;
        set { if (SetProperty(ref endTime, value)) Changed(); }
    }
    public string PreviewText { get => previewText; private set => SetProperty(ref previewText, value); }
    public string NormalizedTimeText { get => normalizedTimeText; private set => SetProperty(ref normalizedTimeText, value); }
    public bool HasErrors => errors.Count != 0;
    public string ServiceError => Error("ServiceId");
    public string TimeCategoryError => Error("TimeCategoryId");
    public string WorkMinutesError => Error("WorkMinutes");
    public string StartTimeError => Error("StartTime");
    public string EndTimeError => Error("EndTime");
    public string TaskIssueMessage => string.Join(Environment.NewLine, errors
        .Where(pair => pair.Key is not "ServiceId" and not "TimeCategoryId" and not "WorkMinutes" and not "StartTime" and not "EndTime")
        .SelectMany(pair => pair.Value)
        .Distinct(StringComparer.Ordinal));
    public bool HasTaskIssues => !string.IsNullOrWhiteSpace(TaskIssueMessage);

    public void UpdateOptions(
        IReadOnlyList<PresetOptionViewModel> newPresets,
        IReadOnlyList<ServiceOptionViewModel> newServices,
        IReadOnlyList<SnapshotTimeCategory> newTimeCategories)
    {
        var serviceId = SelectedService?.Id;
        var categoryId = SelectedTimeCategory?.Id;
        var presetId = SelectedPreset?.Id;
        suppressChanges = true;
        try
        {
            presetCandidates = newPresets;
            services = newServices;
            allTimeCategories = newTimeCategories;
            selectedPreset = newPresets.FirstOrDefault(preset => preset.Id == presetId);
            selectedService = newServices.FirstOrDefault(service => service.Id == serviceId);
            RebuildTimeCategories(categoryId);
            selectedTimeCategory = FindTimeCategory(categoryId);
        }
        finally
        {
            suppressChanges = false;
        }
        OnPropertyChanged(nameof(PresetCandidates));
        OnPropertyChanged(nameof(Services));
        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(SelectedService));
        OnPropertyChanged(nameof(SelectedTimeCategory));
        RefreshDateRules();
    }

    public void UpdatePosition(int index, int count)
    {
        displayOrder = index;
        taskCount = count;
        OnPropertyChanged(nameof(DisplayOrder));
        OnPropertyChanged(nameof(TaskTitle));
        OnPropertyChanged(nameof(AccessibilityText));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(DeleteAccessibilityText));
        NotifyCommands();
    }

    public void RefreshDateRules() => OnPropertyChanged(nameof(ShowStartTime));

    public void ApplyPreview(
        WorkTaskPreviewDto? normalized,
        TaskSalaryCalculation? calculation,
        JapaneseDisplayFormatter formatter)
    {
        if (normalized is not null)
            ApplyNormalized(normalized.NormalizedWorkMinutes, normalized.NormalizedStartTime, normalized.NormalizedEndTime, formatter);
        if (calculation?.Status == SalaryCalculationStatus.Calculated && calculation.TaskSubtotal is { } subtotal)
            PreviewText = $"タスク給与 {formatter.Money(subtotal)}";
        else if (calculation?.Status == SalaryCalculationStatus.Uncalculated)
            PreviewText = "このタスクは設定不足のため未計算です。";
        else
            PreviewText = "入力を修正するとタスク給与を表示します。";
    }

    public void ApplyNormalized(WorkTaskDto task, JapaneseDisplayFormatter formatter) =>
        ApplyNormalized(task.WorkMinutes, task.StartTime, task.EndTime, formatter);

    public void ResetPreview()
    {
        PreviewText = "入力後にタスク給与を表示します。";
        NormalizedTimeText = string.Empty;
    }

    public void SetErrors(IEnumerable<KeyValuePair<string, string>> values)
    {
        errors.Clear();
        foreach (var (field, message) in values) AddErrorCore(field, message);
        NotifyErrors();
    }

    public void AddError(string field, string message)
    {
        AddErrorCore(field, message);
        NotifyErrors();
    }

    public void NotifyCommands()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private void ApplyPreset(PresetOptionViewModel preset)
    {
        suppressChanges = true;
        try
        {
            selectedService = services.FirstOrDefault(service => service.Id == preset.ServiceId);
            OnPropertyChanged(nameof(SelectedService));
            RebuildTimeCategories(preset.TimeCategoryId);
            selectedTimeCategory = FindTimeCategory(preset.TimeCategoryId);
            OnPropertyChanged(nameof(SelectedTimeCategory));
            workMinutesText = preset.DefaultWorkMinutes.Value.ToString();
            OnPropertyChanged(nameof(WorkMinutesText));
            OnPropertyChanged(nameof(ShowStartTime));
        }
        finally
        {
            suppressChanges = false;
        }
    }

    private void RebuildTimeCategories(TimeCategoryId? preferredId)
    {
        timeCategories = new[] { TimeCategoryOptionViewModel.Arbitrary }
            .Concat(allTimeCategories
                .Where(category => category.ServiceId == SelectedService?.Id &&
                    (category.IsEnabled || (category.ServiceId == OriginalServiceId && category.Id == OriginalTimeCategoryId)))
                .OrderBy(category => category.DisplayOrder.Value)
                .ThenBy(category => category.DisplayName, StringComparer.CurrentCulture)
                .Select(category => new TimeCategoryOptionViewModel(
                    category.Id, category.DisplayName, category.StandardMinutes, category.IsEnabled)))
            .ToArray();
        selectedTimeCategory = FindTimeCategory(preferredId);
        OnPropertyChanged(nameof(TimeCategories));
        OnPropertyChanged(nameof(SelectedTimeCategory));
        OnPropertyChanged(nameof(HasTimeCategories));
    }

    private TimeCategoryOptionViewModel? FindTimeCategory(TimeCategoryId? id) =>
        id is null ? null : timeCategories.FirstOrDefault(category => category.Id == id);

    private void ApplyNormalized(
        WorkMinutes? minutes,
        MinuteOfDay? start,
        MinuteOfDay? end,
        JapaneseDisplayFormatter formatter)
    {
        var values = new List<string>();
        if (minutes is { } workMinutes) values.Add($"正規化後: {formatter.Duration(workMinutes)}");
        if (start is { } startTimeValue) values.Add($"開始 {formatter.Time(startTimeValue)}");
        if (end is { } endTimeValue)
            values.Add($"終了 {formatter.Time(endTimeValue)}{(start is { } s && endTimeValue.Value <= s.Value ? "（翌日）" : string.Empty)}");
        NormalizedTimeText = string.Join(" / ", values);
    }

    private void AddErrorCore(string field, string message)
    {
        if (!errors.TryGetValue(field, out var messages)) errors[field] = messages = [];
        if (!messages.Contains(message, StringComparer.Ordinal)) messages.Add(message);
    }

    private string Error(string field) => errors.TryGetValue(field, out var messages)
        ? string.Join(Environment.NewLine, messages)
        : string.Empty;

    private void NotifyErrors()
    {
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(ServiceError));
        OnPropertyChanged(nameof(TimeCategoryError));
        OnPropertyChanged(nameof(WorkMinutesError));
        OnPropertyChanged(nameof(StartTimeError));
        OnPropertyChanged(nameof(EndTimeError));
        OnPropertyChanged(nameof(TaskIssueMessage));
        OnPropertyChanged(nameof(HasTaskIssues));
    }

    private void Changed()
    {
        if (!suppressChanges) inputChanged(this);
    }
}

public sealed record PresetOptionViewModel(
    ServicePresetId Id,
    string DisplayName,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkMinutes DefaultWorkMinutes,
    bool IsAvailable,
    string IssueText)
{
    public string PickerText => IsAvailable ? DisplayName : $"{DisplayName}（利用不可）";
}

public sealed record ServiceOptionViewModel(ServiceId Id, string DisplayName, bool IsEnabled)
{
    public string PickerText => IsEnabled ? DisplayName : $"{DisplayName}（現在は利用不可）";
}

public sealed record TimeCategoryOptionViewModel(TimeCategoryId? Id, string DisplayName, WorkMinutes? StandardMinutes, bool IsEnabled)
{
    public static TimeCategoryOptionViewModel Arbitrary { get; } = new(null, "任意の時間で入力", null, true);
    public string PickerText => IsEnabled ? DisplayName : $"{DisplayName}（現在は利用不可）";
}

public sealed record WorkInputModeOption(WorkInputMode Value, string DisplayName)
{
    public static WorkInputModeOption Duration { get; } = new(WorkInputMode.Duration, "勤務時間");
    public static WorkInputModeOption TimeRange { get; } = new(WorkInputMode.TimeRange, "開始・終了時刻");
}
