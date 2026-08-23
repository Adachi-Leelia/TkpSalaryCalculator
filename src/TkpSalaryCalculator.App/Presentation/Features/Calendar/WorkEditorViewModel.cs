using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Calendar;

/// <summary>SCR-WORK-01 の入力、プレビュー、保存、および項目エラーを管理します。</summary>
public sealed class WorkEditorViewModel : EditableViewModelBase
{
    private readonly IWorkRecordUseCase workRecords;
    private readonly ICalendarNavigator navigator;
    private readonly IssuePresenter issuePresenter;
    private readonly JapaneseDisplayFormatter formatter;
    private readonly IAppSessionState sessionState;
    private readonly Guid operationId = Guid.NewGuid();
    private bool isInitializing;
    private bool hasSaved;
    private WorkRecordId? workRecordId;
    private ServiceId? originalServiceId;
    private TimeCategoryId? originalTimeCategoryId;
    private ServicePresetId? originalSourceServicePresetId;
    private DateOnly optionsDate;
    private DateTime workDate;
    private IReadOnlyList<PresetOptionViewModel> presetCandidates = [];
    private IReadOnlyList<ServiceOptionViewModel> services = [];
    private IReadOnlyList<TimeCategoryOptionViewModel> timeCategories = [];
    private PresetOptionViewModel? selectedPreset;
    private ServiceOptionViewModel? selectedService;
    private TimeCategoryOptionViewModel? selectedTimeCategory;
    private WorkInputModeOption selectedInputMode = WorkInputModeOption.Duration;
    private string workMinutesText = "60";
    private TimeSpan startTime = new(9, 0, 0);
    private TimeSpan endTime = new(10, 0, 0);
    private string previewText = "入力後に給与をプレビューできます。";
    private string issueMessage = string.Empty;
    private IReadOnlyDictionary<string, string> fieldErrors = new Dictionary<string, string>();
    private bool canSave;
    private string normalizedTimeText = string.Empty;
    private string unavailableCandidatesText = string.Empty;
    private string? firstInvalidField;
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
        PreviewCommand = new AsyncCommand(PreviewAsync, PresentError, () => SelectedService is not null);
        SaveCommand = new AsyncCommand(SaveAsync, PresentError, () => !hasSaved && SelectedService is not null && IsNotBusy);
    }

    public bool IsEditing => WorkRecordId is not null;
    public string PageTitle => IsEditing ? "勤務を編集" : "勤務を追加";
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
            OnPropertyChanged(nameof(ShowStartTime));
            InputChanged();
        }
    }
    public string SettingsMonthText => $"適用する設定対象年月: {WorkDate:yyyy年M月}";
    public IReadOnlyList<PresetOptionViewModel> PresetCandidates
    {
        get => presetCandidates;
        private set => SetProperty(ref presetCandidates, value);
    }
    public IReadOnlyList<ServiceOptionViewModel> Services
    {
        get => services;
        private set => SetProperty(ref services, value);
    }
    public IReadOnlyList<TimeCategoryOptionViewModel> TimeCategories
    {
        get => timeCategories;
        private set
        {
            if (!SetProperty(ref timeCategories, value)) return;
            OnPropertyChanged(nameof(HasTimeCategories));
        }
    }
    public bool HasTimeCategories => TimeCategories.Count > 1;
    public IReadOnlyList<WorkInputModeOption> InputModes { get; }
    public PresetOptionViewModel? SelectedPreset
    {
        get => selectedPreset;
        set
        {
            if (!SetProperty(ref selectedPreset, value) || value is null) return;
            if (!value.IsAvailable)
            {
                IssueMessage = value.IssueText;
                return;
            }
            ApplyPreset(value);
        }
    }
    public ServiceOptionViewModel? SelectedService
    {
        get => selectedService;
        set
        {
            if (!SetProperty(ref selectedService, value)) return;
            RebuildTimeCategories(value?.Id);
            OnPropertyChanged(nameof(ShowStartTime));
            SaveCommand.NotifyCanExecuteChanged();
            InputChanged();
        }
    }
    public TimeCategoryOptionViewModel? SelectedTimeCategory
    {
        get => selectedTimeCategory;
        set
        {
            if (!SetProperty(ref selectedTimeCategory, value)) return;
            if (value?.StandardMinutes is { } standardMinutes) WorkMinutesText = standardMinutes.Value.ToString();
            InputChanged();
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
            InputChanged();
        }
    }
    public bool ShowDuration => SelectedInputMode.Value == WorkInputMode.Duration;
    public bool ShowEndTime => SelectedInputMode.Value == WorkInputMode.TimeRange;
    public bool ShowStartTime => ShowEndTime || SelectedService is { } service && HasApplicableTimedPremium(service.Id);
    public string WorkMinutesText
    {
        get => workMinutesText;
        set { if (SetProperty(ref workMinutesText, value)) InputChanged(); }
    }
    public TimeSpan StartTime
    {
        get => startTime;
        set { if (SetProperty(ref startTime, value)) InputChanged(); }
    }
    public TimeSpan EndTime
    {
        get => endTime;
        set { if (SetProperty(ref endTime, value)) InputChanged(); }
    }
    public string PreviewText
    {
        get => previewText;
        private set => SetProperty(ref previewText, value);
    }
    public string NormalizedTimeText
    {
        get => normalizedTimeText;
        private set => SetProperty(ref normalizedTimeText, value);
    }
    public string IssueMessage
    {
        get => issueMessage;
        private set
        {
            if (!SetProperty(ref issueMessage, value)) return;
            OnPropertyChanged(nameof(HasIssues));
        }
    }
    public bool HasIssues => !string.IsNullOrWhiteSpace(IssueMessage) || FieldErrors.Count != 0;
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
            OnPropertyChanged(nameof(HasIssues));
        }
    }
    public string ServiceError => FieldErrors.GetValueOrDefault("ServiceId", string.Empty);
    public string TimeCategoryError => FieldErrors.GetValueOrDefault("TimeCategoryId", string.Empty);
    public string WorkMinutesError => FieldErrors.GetValueOrDefault("WorkMinutes", string.Empty);
    public string StartTimeError => FieldErrors.GetValueOrDefault("StartTime", string.Empty);
    public string EndTimeError => FieldErrors.GetValueOrDefault("EndTime", string.Empty);
    public string? FirstInvalidField
    {
        get => firstInvalidField;
        private set => SetProperty(ref firstInvalidField, value);
    }
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
    public AsyncCommand ReloadCommand { get; }
    public AsyncCommand PreviewCommand { get; }
    public AsyncCommand SaveCommand { get; }

    public void Initialize(DateOnly date, WorkRecordId? id)
    {
        InvalidateTrackedLoad();
        hasSaved = false;
        editorScreen = null;
        previewedCommand = null;
        originalServiceId = null;
        originalTimeCategoryId = null;
        originalSourceServicePresetId = null;
        WorkRecordId = id;
        workDate = date.ToDateTime(TimeOnly.MinValue);
        OnPropertyChanged(nameof(WorkDate));
        OnPropertyChanged(nameof(SettingsMonthText));
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

            var result = await workRecords.SaveAsync(command, cancellationToken);
            sessionState.NotifyDataChanged(AppDataChangeKind.WorkRecords | AppDataChangeKind.BackupStatus);
            ApplyNormalized(result.WorkRecord.WorkMinutes, result.WorkRecord.StartTime, result.WorkRecord.EndTime);
            hasSaved = true;
            MarkSaved();
            await navigator.GoBackAsync("勤務記録を保存しました。", cancellationToken);
        });
        NotifyCommands();
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        isInitializing = true;
        try
        {
            var date = DateOnly.FromDateTime(WorkDate);
            editorScreen = await workRecords.GetEditorScreenAsync(date, WorkRecordId, cancellationToken);
            var options = editorScreen.InputOptions;
            var existing = editorScreen.ExistingRecord;
            if (WorkRecordId is not null && existing is null)
                throw new InvalidOperationException("編集する勤務記録が見つかりませんでした。");

            if (existing is not null) RememberOriginalSelection(existing);
            PopulateOptions(options, editorScreen.HolidayCalendar);
            if (existing is not null)
                ApplyExisting(existing);
            else if (options.SuggestedValues is { } suggested && IsAvailableForNewRecord(suggested.ServiceId, suggested.TimeCategoryId))
                ApplySuggested(suggested);
            else if (PresetCandidates.FirstOrDefault(x => x.IsAvailable) is { } first)
            {
                selectedPreset = first;
                OnPropertyChanged(nameof(SelectedPreset));
                ApplyPreset(first);
            }
            else if (Services.FirstOrDefault(x => x.IsEnabled) is { } service)
                SelectedService = service;
            MarkSaved();
        }
        finally
        {
            isInitializing = false;
        }

        await PreviewCoreAsync(cancellationToken);
        MarkSaved();
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
        var serviceId = SelectedService?.Id;
        var categoryId = SelectedTimeCategory?.Id;
        isInitializing = true;
        try
        {
            editorScreen = await workRecords.GetEditorScreenAsync(selectedDate, WorkRecordId, cancellationToken);
            var options = editorScreen.InputOptions;
            PopulateOptions(options, editorScreen.HolidayCalendar);
            selectedPreset = PresetCandidates.FirstOrDefault(x => x.Id == selectedPreset?.Id && x.IsAvailable);
            OnPropertyChanged(nameof(SelectedPreset));
            SelectedService = Services.FirstOrDefault(x => x.Id == serviceId) ?? Services.FirstOrDefault(x => x.IsEnabled);
            SelectedTimeCategory = TimeCategories.FirstOrDefault(x => x.Id == categoryId) ?? TimeCategories.FirstOrDefault();
        }
        finally
        {
            isInitializing = false;
        }
    }

    private void PopulateOptions(WorkInputOptionsDto options, HolidayCalendar holidayCalendar)
    {
        optionsDate = options.WorkDate;
        this.holidayCalendar = holidayCalendar;
        PresetCandidates = options.PresetCandidates.Where(x => x.IsAvailable).Select(x => new PresetOptionViewModel(
            x.Preset.Id,
            x.Preset.DisplayName,
            x.Preset.ServiceId,
            x.Preset.TimeCategoryId,
            x.Preset.DefaultWorkMinutes,
            x.IsAvailable,
            string.Join(Environment.NewLine, x.Issues.Select(issue => issue.Message)))).ToArray();
        Services = options.Settings.Snapshot.Services
            .Where(x => x.IsEnabled || x.Id == originalServiceId)
            .OrderBy(x => x.DisplayOrder.Value)
            .Select(x => new ServiceOptionViewModel(x.Id, x.DisplayName, x.IsEnabled))
            .ToArray();
        allTimeCategories = options.Settings.Snapshot.TimeCategories;
        premiums = options.Settings.Snapshot.Premiums;
        UnavailableCandidatesText = string.Join(Environment.NewLine,
            options.PresetCandidates.Where(x => !x.IsAvailable).Select(FormatUnavailableCandidate));
    }

    private static string FormatUnavailableCandidate(ServicePresetCandidateDto value)
    {
        var reason = value.Issues.Count == 0
            ? "この候補は現在無効です。設定で有効にしてください。"
            : string.Join(Environment.NewLine, value.Issues.Select(issue => issue.Message));
        return $"利用できない候補: {value.Preset.DisplayName}: {reason}";
    }

    private IReadOnlyList<SnapshotTimeCategory> allTimeCategories = [];
    private IReadOnlyList<SnapshotPremium> premiums = [];
    private HolidayCalendar? holidayCalendar;

    private void ApplyExisting(WorkRecordDto value)
    {
        SelectedService = Services.FirstOrDefault(x => x.Id == value.ServiceId);
        SelectedTimeCategory = TimeCategories.FirstOrDefault(x => x.Id == value.TimeCategoryId);
        SelectedInputMode = InputModes.First(x => x.Value == value.InputMode);
        WorkMinutesText = value.WorkMinutes.Value.ToString();
        if (value.StartTime is { } start) StartTime = ToTimeSpan(start);
        if (value.EndTime is { } end) EndTime = ToTimeSpan(end);
        selectedPreset = PresetCandidates.FirstOrDefault(x => x.Id == value.SourceServicePresetId);
        OnPropertyChanged(nameof(SelectedPreset));
    }

    private void RememberOriginalSelection(WorkRecordDto value)
    {
        originalServiceId = value.ServiceId;
        originalTimeCategoryId = value.TimeCategoryId;
        originalSourceServicePresetId = value.SourceServicePresetId;
    }

    private void ApplySuggested(SaveWorkRecordCommand value)
    {
        SelectedService = Services.FirstOrDefault(x => x.Id == value.ServiceId) ?? Services.FirstOrDefault();
        SelectedTimeCategory = TimeCategories.FirstOrDefault(x => x.Id == value.TimeCategoryId);
        SelectedInputMode = InputModes.First(x => x.Value == value.InputMode);
        if (value.WorkMinutes is { } minutes) WorkMinutesText = minutes.Value.ToString();
        if (value.StartTime is { } start) StartTime = ToTimeSpan(start);
        if (value.EndTime is { } end) EndTime = ToTimeSpan(end);
        selectedPreset = PresetCandidates.FirstOrDefault(x => x.Id == value.SourceServicePresetId);
        OnPropertyChanged(nameof(SelectedPreset));
    }

    private void ApplyPreset(PresetOptionViewModel value)
    {
        isInitializing = true;
        try
        {
            SelectedService = Services.FirstOrDefault(x => x.Id == value.ServiceId);
            SelectedTimeCategory = TimeCategories.FirstOrDefault(x => x.Id == value.TimeCategoryId);
            WorkMinutesText = value.DefaultWorkMinutes.Value.ToString();
        }
        finally
        {
            isInitializing = false;
        }
        InputChanged();
    }

    private void RebuildTimeCategories(ServiceId? serviceId)
    {
        var previous = SelectedTimeCategory;
        TimeCategories = new[] { TimeCategoryOptionViewModel.Arbitrary }
            .Concat(allTimeCategories
            .Where(x => x.ServiceId == serviceId &&
                (x.IsEnabled || (serviceId == originalServiceId && x.Id == originalTimeCategoryId)))
            .OrderBy(x => x.DisplayOrder.Value)
            .Select(x => new TimeCategoryOptionViewModel(x.Id, x.DisplayName, x.StandardMinutes, x.IsEnabled)))
            .ToArray();
        selectedTimeCategory = previous is { Id: null }
            ? TimeCategories[0]
            : TimeCategories.FirstOrDefault(x => x.Id == previous?.Id) ??
              TimeCategories.FirstOrDefault(x => x.Id is not null && x.IsEnabled) ?? TimeCategories[0];
        OnPropertyChanged(nameof(SelectedTimeCategory));
        if (!isInitializing && selectedTimeCategory.StandardMinutes is { } standardMinutes)
            WorkMinutesText = standardMinutes.Value.ToString();
    }

    private bool HasApplicableTimedPremium(ServiceId serviceId)
    {
        var date = DateOnly.FromDateTime(WorkDate);
        return premiums.Any(x =>
        {
            if (!x.IsEnabled || x.StartTime is null ||
                (x.ServiceIds.Count != 0 && !x.ServiceIds.Contains(serviceId))) return false;
            var hasDateCondition = x.Weekdays.Count != 0 || x.Dates.Count != 0 || x.UsesNationalHolidays;
            return !hasDateCondition || x.Weekdays.Contains(date.DayOfWeek) || x.Dates.Contains(date) ||
                (x.UsesNationalHolidays && holidayCalendar?.Holidays.ContainsKey(date) == true);
        });
    }

    private bool IsAvailableForNewRecord(ServiceId serviceId, TimeCategoryId? timeCategoryId) =>
        Services.Any(x => x.Id == serviceId && x.IsEnabled) &&
        (timeCategoryId is null || allTimeCategories.Any(x =>
            x.Id == timeCategoryId && x.ServiceId == serviceId && x.IsEnabled));

    private SaveWorkRecordCommand? BuildCommand(out IReadOnlyList<IssueDto> issues)
    {
        var local = new List<IssueDto>();
        if (SelectedService is null)
            local.Add(new IssueDto("WORK_SERVICE_REQUIRED", "ServiceId", "サービスを選択してください。"));

        WorkMinutes? minutes = null;
        if (SelectedInputMode.Value == WorkInputMode.Duration)
        {
            if (!int.TryParse(WorkMinutesText, out var value) || value is < 1 or > 1440)
                local.Add(new IssueDto("WORK_MINUTES_OUT_OF_RANGE", "WorkMinutes", "勤務時間は1分以上24時間以内で入力してください。超える場合は複数の記録に分けてください。"));
            else
                minutes = new WorkMinutes(value);
        }

        if (local.Count != 0)
        {
            issues = local;
            return null;
        }

        issues = [];
        var mode = SelectedInputMode.Value;
        return new SaveWorkRecordCommand(
            WorkRecordId,
            DateOnly.FromDateTime(WorkDate),
            SelectedService!.Id,
            SelectedTimeCategory?.Id,
            mode,
            minutes,
            mode == WorkInputMode.TimeRange || ShowStartTime ? ToMinuteOfDay(StartTime) : null,
            mode == WorkInputMode.TimeRange ? ToMinuteOfDay(EndTime) : null,
            SelectedPreset?.Id ?? (IsEditing ? originalSourceServicePresetId : null),
            WorkRecordId is null ? operationId : null);
    }

    private void ApplyPreview(WorkRecordPreviewDto preview)
    {
        PresentIssues(preview.Issues);
        CanSave = preview.CanSave;
        if (preview.Calculation?.Status == SalaryCalculationStatus.Calculated && preview.Calculation.Total is { } total)
            PreviewText = $"給与見込み {formatter.Money(total)}";
        else if (preview.Calculation?.Status == SalaryCalculationStatus.Uncalculated)
            PreviewText = "未計算（勤務内容は保存できます）";
        else
            PreviewText = "入力を修正すると給与をプレビューできます。";
        ApplyNormalized(preview.NormalizedWorkMinutes, preview.NormalizedStartTime, preview.NormalizedEndTime);
    }

    private void ApplyNormalized(WorkMinutes? minutes, MinuteOfDay? start, MinuteOfDay? end)
    {
        var values = new List<string>();
        if (minutes is { } workMinutes) values.Add($"正規化後: {formatter.Duration(workMinutes)}");
        if (start is { } startTime) values.Add($"開始 {formatter.Time(startTime)}");
        if (end is { } endTime) values.Add($"終了 {formatter.Time(endTime)}{(start is { } s && endTime.Value <= s.Value ? "（翌日）" : string.Empty)}");
        NormalizedTimeText = string.Join(" / ", values);
    }

    private void PresentIssues(IReadOnlyList<IssueDto> issues)
    {
        var presentation = issuePresenter.Present(issues);
        FieldErrors = presentation.FieldErrors;
        IssueMessage = presentation.ScreenMessage ?? string.Empty;
        FirstInvalidField = presentation.FirstInvalidField;
    }

    private void InputChanged()
    {
        if (isInitializing) return;
        previewedCommand = null;
        MarkDirty();
        CanSave = false;
        PreviewText = "入力内容が変わりました。給与を再プレビューしてください。";
        NormalizedTimeText = string.Empty;
        FieldErrors = new Dictionary<string, string>();
        IssueMessage = string.Empty;
        FirstInvalidField = null;
        PreviewCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void NotifyCommands()
    {
        PreviewCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private static MinuteOfDay ToMinuteOfDay(TimeSpan value) => new((int)value.TotalMinutes);
    private static TimeSpan ToTimeSpan(MinuteOfDay value) => TimeSpan.FromMinutes(value.Value);
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
