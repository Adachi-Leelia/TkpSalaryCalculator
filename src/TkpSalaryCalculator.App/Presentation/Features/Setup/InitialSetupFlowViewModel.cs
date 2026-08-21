using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Setup;

public enum InitialSetupStep
{
    Welcome,
    ClosingDay,
    Services,
    Additions,
    Confirmation,
}

public static class InitialSetupStepIds
{
    public const string Welcome = "welcome";
    public const string ClosingDay = "closing-day";
    public const string Services = "services";
    public const string Additions = "additions";
    public const string Confirmation = "confirmation";

    public static string FromStep(InitialSetupStep step) => step switch
    {
        InitialSetupStep.Welcome => Welcome,
        InitialSetupStep.ClosingDay => ClosingDay,
        InitialSetupStep.Services => Services,
        InitialSetupStep.Additions => Additions,
        InitialSetupStep.Confirmation => Confirmation,
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    public static InitialSetupStep ToStep(string? step) => step switch
    {
        ClosingDay => InitialSetupStep.ClosingDay,
        Services => InitialSetupStep.Services,
        Additions => InitialSetupStep.Additions,
        Confirmation => InitialSetupStep.Confirmation,
        _ => InitialSetupStep.Welcome,
    };
}

/// <summary>SCR-INIT-01～05 の再開可能な初期設定フローを統括します。</summary>
public sealed class InitialSetupFlowViewModel : ViewModelBase
{
    private readonly IInitialSetupUseCase initialSetup;
    private readonly IMonthSettingsUseCase monthSettings;
    private readonly IServicePresetUseCase presets;
    private readonly IPayrollPeriodSettingsUseCase payrollPeriods;
    private readonly IAppRootNavigator rootNavigator;
    private readonly IAppSessionState sessionState;
    private readonly JapaneseDisplayFormatter formatter;
    private readonly DateOnly localToday;
    private readonly YearMonth setupMonth;
    private readonly AsyncCommand nextCommand;
    private readonly AsyncCommand backCommand;
    private readonly AsyncCommand skipAdditionsCommand;
    private readonly AsyncCommand previewClosingDayCommand;
    private InitialSetupStep currentStep;
    private bool initialized;
    private bool canComplete;
    private string? resumeMessage;
    private string? missingRequirements;
    private string closingSummary = "未設定";
    private string serviceSummary = "使用可能なサービス設定はありません。";
    private string additionsSummary = "使用する加算はありません。";

    public InitialSetupFlowViewModel(
        IInitialSetupUseCase initialSetup,
        IMonthSettingsUseCase monthSettings,
        IServicePresetUseCase presets,
        IPayrollPeriodSettingsUseCase payrollPeriods,
        IAppRootNavigator rootNavigator,
        IAppSessionState sessionState,
        IUtcClock clock,
        ILocalDateConverter localDates,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        this.initialSetup = initialSetup ?? throw new ArgumentNullException(nameof(initialSetup));
        this.monthSettings = monthSettings ?? throw new ArgumentNullException(nameof(monthSettings));
        this.presets = presets ?? throw new ArgumentNullException(nameof(presets));
        this.payrollPeriods = payrollPeriods ?? throw new ArgumentNullException(nameof(payrollPeriods));
        this.rootNavigator = rootNavigator ?? throw new ArgumentNullException(nameof(rootNavigator));
        this.sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(localDates);
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));

        localToday = localDates.ToLocalDate(clock.UtcNow);
        setupMonth = new YearMonth(localToday.Year, localToday.Month);
        Welcome = new WelcomeStepViewModel();
        ClosingDay = new ClosingDayStepViewModel();
        Services = new ServiceRatesStepViewModel();
        Additions = new AdditionsStepViewModel();
        Confirmation = new SetupConfirmationStepViewModel();
        currentStep = InitialSetupStepIds.ToStep(sessionState.InitialSetupState?.Step);

        nextCommand = new AsyncCommand(MoveNextAsync, PresentError, () => CurrentStep != InitialSetupStep.Confirmation || CanComplete);
        backCommand = new AsyncCommand(MoveBackAsync, PresentError, () => CanGoBack);
        skipAdditionsCommand = new AsyncCommand(SkipAdditionsAsync, PresentError, () => CurrentStep == InitialSetupStep.Additions);
        previewClosingDayCommand = new AsyncCommand(PreviewClosingDayAsync, PresentError,
            () => CurrentStep == InitialSetupStep.ClosingDay && ClosingDay.SelectedOption is not null);
        Additions.SkipCommand = skipAdditionsCommand;
        ClosingDay.PreviewCommand = previewClosingDayCommand;
    }

    public WelcomeStepViewModel Welcome { get; }

    public ClosingDayStepViewModel ClosingDay { get; }

    public ServiceRatesStepViewModel Services { get; }

    public AdditionsStepViewModel Additions { get; }

    public SetupConfirmationStepViewModel Confirmation { get; }

    public InitialSetupStep CurrentStep
    {
        get => currentStep;
        private set
        {
            if (!SetProperty(ref currentStep, value)) return;
            OnPropertyChanged(nameof(IsWelcomeStep));
            OnPropertyChanged(nameof(IsClosingDayStep));
            OnPropertyChanged(nameof(IsServicesStep));
            OnPropertyChanged(nameof(IsAdditionsStep));
            OnPropertyChanged(nameof(IsConfirmationStep));
            OnPropertyChanged(nameof(StepTitle));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(PrimaryActionText));
            OnPropertyChanged(nameof(CanGoBack));
            nextCommand.NotifyCanExecuteChanged();
            backCommand.NotifyCanExecuteChanged();
            skipAdditionsCommand.NotifyCanExecuteChanged();
            previewClosingDayCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsWelcomeStep => CurrentStep == InitialSetupStep.Welcome;

    public bool IsClosingDayStep => CurrentStep == InitialSetupStep.ClosingDay;

    public bool IsServicesStep => CurrentStep == InitialSetupStep.Services;

    public bool IsAdditionsStep => CurrentStep == InitialSetupStep.Additions;

    public bool IsConfirmationStep => CurrentStep == InitialSetupStep.Confirmation;

    public string StepTitle => CurrentStep switch
    {
        InitialSetupStep.Welcome => "はじめに",
        InitialSetupStep.ClosingDay => "締め日",
        InitialSetupStep.Services => "サービスと単価",
        InitialSetupStep.Additions => "加算",
        InitialSetupStep.Confirmation => "設定確認",
        _ => string.Empty,
    };

    public string ProgressText => $"{(int)CurrentStep + 1} / 5";

    public string PrimaryActionText => CurrentStep == InitialSetupStep.Confirmation ? "設定を完了する" :
        CurrentStep == InitialSetupStep.Welcome ? "設定を始める" : "保存して次へ";

    public bool CanGoBack => CurrentStep != InitialSetupStep.Welcome;

    public bool CanComplete
    {
        get => canComplete;
        private set
        {
            if (!SetProperty(ref canComplete, value)) return;
            nextCommand.NotifyCanExecuteChanged();
        }
    }

    public string? ResumeMessage
    {
        get => resumeMessage;
        private set
        {
            if (!SetProperty(ref resumeMessage, value)) return;
            OnPropertyChanged(nameof(HasResumeMessage));
        }
    }

    public bool HasResumeMessage => !string.IsNullOrWhiteSpace(ResumeMessage);

    public string? MissingRequirements
    {
        get => missingRequirements;
        private set
        {
            if (!SetProperty(ref missingRequirements, value)) return;
            OnPropertyChanged(nameof(HasMissingRequirements));
        }
    }

    public bool HasMissingRequirements => !string.IsNullOrWhiteSpace(MissingRequirements);

    public string ClosingSummary
    {
        get => closingSummary;
        private set => SetProperty(ref closingSummary, value);
    }

    public string ServiceSummary
    {
        get => serviceSummary;
        private set => SetProperty(ref serviceSummary, value);
    }

    public string AdditionsSummary
    {
        get => additionsSummary;
        private set => SetProperty(ref additionsSummary, value);
    }

    public ICommand NextCommand => nextCommand;

    public ICommand BackCommand => backCommand;

    public ICommand SkipAdditionsCommand => skipAdditionsCommand;

    public Task InitializeAsync()
    {
        if (initialized) return Task.CompletedTask;
        return RunBusyAsync(InitializeCoreAsync);
    }

    public Task MoveNextAsync() => RunBusyAsync(MoveNextCoreAsync);

    public Task MoveBackAsync() => RunBusyAsync(MoveBackCoreAsync);

    public Task SkipAdditionsAsync() => RunBusyAsync(async cancellationToken =>
    {
        if (CurrentStep != InitialSetupStep.Additions) return;
        Additions.DisableAll();
        await SaveAdditionsAsync(cancellationToken).ConfigureAwait(false);
        await MoveToAsync(InitialSetupStep.Confirmation, cancellationToken).ConfigureAwait(false);
        await RefreshConfirmationAsync(cancellationToken).ConfigureAwait(false);
    });

    public Task PreviewClosingDayAsync() => RunBusyAsync(async cancellationToken =>
    {
        if (ClosingDay.SelectedOption is null) return;
        var preview = await GetClosingDayPreviewAsync(cancellationToken).ConfigureAwait(false);
        ClosingDay.SetPreview(preview.ReplacementPeriod, formatter);
    });

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        var state = await initialSetup.GetStateAsync(cancellationToken).ConfigureAwait(false);
        sessionState.InitialSetupState = state;
        CurrentStep = InitialSetupStepIds.ToStep(state.Step);
        ResumeMessage = state.Status == InitialSetupStatus.InProgress && !string.IsNullOrWhiteSpace(state.Step)
            ? $"保存済みの「{StepTitle}」から再開しました。"
            : null;

        var settings = await monthSettings.GetAsync(setupMonth, cancellationToken).ConfigureAwait(false);
        var presetValues = await presets.GetAllAsync(cancellationToken).ConfigureAwait(false);
        Services.Load(settings.Snapshot, presetValues);
        Additions.Load(settings.Snapshot);
        await LoadClosingDayAsync(cancellationToken).ConfigureAwait(false);
        if (CurrentStep == InitialSetupStep.Confirmation)
            await RefreshConfirmationAsync(cancellationToken).ConfigureAwait(false);
        else
            ApplyIssues(state.Issues);
        initialized = true;
    }

    private async Task LoadClosingDayAsync(CancellationToken cancellationToken)
    {
        var lookupKey = new PayrollPeriodKey(setupMonth);
        var existing = await payrollPeriods.GetClosingRuleAsync(lookupKey, cancellationToken).ConfigureAwait(false);
        if (existing is null) return;

        ClosingDay.Select(existing.ClosingDay);
        var period = await payrollPeriods.FindPeriodAsync(localToday, cancellationToken).ConfigureAwait(false);
        ClosingDay.SetPreview(period, formatter);
        sessionState.PayrollPeriod = period.Key;
    }

    private async Task MoveNextCoreAsync(CancellationToken cancellationToken)
    {
        switch (CurrentStep)
        {
            case InitialSetupStep.Welcome:
                await MoveToAsync(InitialSetupStep.ClosingDay, cancellationToken).ConfigureAwait(false);
                break;
            case InitialSetupStep.ClosingDay:
                await SaveClosingDayAsync(cancellationToken).ConfigureAwait(false);
                await MoveToAsync(InitialSetupStep.Services, cancellationToken).ConfigureAwait(false);
                break;
            case InitialSetupStep.Services:
                await SaveServicesAsync(cancellationToken).ConfigureAwait(false);
                await MoveToAsync(InitialSetupStep.Additions, cancellationToken).ConfigureAwait(false);
                break;
            case InitialSetupStep.Additions:
                await SaveAdditionsAsync(cancellationToken).ConfigureAwait(false);
                await MoveToAsync(InitialSetupStep.Confirmation, cancellationToken).ConfigureAwait(false);
                await RefreshConfirmationAsync(cancellationToken).ConfigureAwait(false);
                break;
            case InitialSetupStep.Confirmation:
                await CompleteAsync(cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task MoveBackCoreAsync(CancellationToken cancellationToken)
    {
        if (!CanGoBack) return;
        var destination = (InitialSetupStep)((int)CurrentStep - 1);
        await MoveToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private async Task MoveToAsync(InitialSetupStep step, CancellationToken cancellationToken)
    {
        var id = InitialSetupStepIds.FromStep(step);
        await initialSetup.SaveProgressAsync(id, cancellationToken).ConfigureAwait(false);
        var state = new InitialSetupStateDto(InitialSetupStatus.InProgress, id,
            sessionState.InitialSetupState?.Issues ?? []);
        sessionState.InitialSetupState = state;
        CurrentStep = step;
        if (step != InitialSetupStep.Confirmation) CanComplete = false;
    }

    private async Task SaveClosingDayAsync(CancellationToken cancellationToken)
    {
        if (ClosingDay.SelectedOption is null)
            throw new ApplicationErrorException("SETUP_CLOSING_DAY_REQUIRED", "締め日を選択してください。", "ClosingDay");

        var closingDay = ClosingDay.SelectedOption.Value;
        var key = GetCurrentPayrollPeriodKey(localToday, closingDay);
        var command = new ReplaceClosingRuleCommand(key, closingDay);
        var preview = await GetClosingDayPreviewAsync(cancellationToken).ConfigureAwait(false);
        ClosingDay.SetPreview(preview.ReplacementPeriod, formatter);
        await payrollPeriods.ReplaceClosingRuleAsync(command, preview.ConfirmationToken, cancellationToken).ConfigureAwait(false);
        sessionState.PayrollPeriod = preview.ReplacementPeriod.Key;
    }

    private Task<ClosingRuleReplacementPreviewDto> GetClosingDayPreviewAsync(CancellationToken cancellationToken)
    {
        var closingDay = ClosingDay.SelectedOption?.Value;
        if (ClosingDay.SelectedOption is null)
            throw new ApplicationErrorException("SETUP_CLOSING_DAY_REQUIRED", "締め日を選択してください。", "ClosingDay");
        var key = GetCurrentPayrollPeriodKey(localToday, closingDay);
        return payrollPeriods.PreviewClosingRuleReplacementAsync(
            new ReplaceClosingRuleCommand(key, closingDay), cancellationToken);
    }

    private async Task SaveServicesAsync(CancellationToken cancellationToken)
    {
        var current = await monthSettings.GetAsync(setupMonth, cancellationToken).ConfigureAwait(false);
        var replacement = Services.BuildReplacement(current.Snapshot);
        var preview = await monthSettings.PreviewReplacementAsync(setupMonth, replacement, cancellationToken).ConfigureAwait(false);
        if (preview.Issues.Count != 0)
            throw new ApplicationErrorException("SETUP_SERVICE_SETTINGS_INVALID",
                string.Join(Environment.NewLine, preview.Issues.Select(issue => issue.Message)));

        await monthSettings.CloneAndReplaceAsync(setupMonth, replacement, preview.ConfirmationToken, cancellationToken)
            .ConfigureAwait(false);
        await Services.SavePresetsAsync(presets, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveAdditionsAsync(CancellationToken cancellationToken)
    {
        var current = await monthSettings.GetAsync(setupMonth, cancellationToken).ConfigureAwait(false);
        var replacement = Additions.BuildReplacement(current.Snapshot);
        var preview = await monthSettings.PreviewReplacementAsync(setupMonth, replacement, cancellationToken).ConfigureAwait(false);
        if (preview.Issues.Count != 0)
            throw new ApplicationErrorException("SETUP_ADDITIONS_INVALID",
                string.Join(Environment.NewLine, preview.Issues.Select(issue => issue.Message)));

        await monthSettings.CloneAndReplaceAsync(setupMonth, replacement, preview.ConfirmationToken, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RefreshConfirmationAsync(CancellationToken cancellationToken)
    {
        var state = await initialSetup.GetStateAsync(cancellationToken).ConfigureAwait(false);
        var settings = await monthSettings.GetAsync(setupMonth, cancellationToken).ConfigureAwait(false);
        sessionState.InitialSetupState = state;
        ApplyIssues(state.Issues);
        CanComplete = state.Issues.Count == 0;

        var period = await TryFindCurrentPeriodAsync(cancellationToken).ConfigureAwait(false);
        var closing = ClosingDay.SelectedOption?.DisplayName ?? "未設定";
        ClosingSummary = period is null
            ? $"締め日: {closing}"
            : $"締め日: {closing}\n{formatter.PayrollPeriod(period)}";

        var enabledCategories = settings.Snapshot.TimeCategories
            .Where(category => category.IsEnabled && settings.Snapshot.Services.Any(service => service.Id == category.ServiceId && service.IsEnabled))
            .ToArray();
        var calculable = enabledCategories.Count(category => settings.Snapshot.Rates.Any(rate =>
            rate.ServiceId == category.ServiceId && (rate.TimeCategoryId is null || rate.TimeCategoryId == category.Id)));
        ServiceSummary = $"使用可能なサービス設定: {enabledCategories.Length}件\n単価設定済み: {calculable}件\n設定不備: {enabledCategories.Length - calculable}件";

        var enabledAdditions = settings.Snapshot.Premiums.Where(value => value.IsEnabled).Select(value => value.DisplayName)
            .Concat(settings.Snapshot.CountBonuses.Where(value => value.IsEnabled).Select(value => value.DisplayName)).ToArray();
        AdditionsSummary = enabledAdditions.Length == 0
            ? "有効な割増・件数加算: なし"
            : $"有効な割増・件数加算: {string.Join("、", enabledAdditions)}";

        Confirmation.Update(ClosingSummary, ServiceSummary, AdditionsSummary, MissingRequirements);
    }

    private async Task<PayrollPeriod?> TryFindCurrentPeriodAsync(CancellationToken cancellationToken)
    {
        try
        {
            var period = await payrollPeriods.FindPeriodAsync(localToday, cancellationToken).ConfigureAwait(false);
            sessionState.PayrollPeriod = period.Key;
            return period;
        }
        catch (ApplicationErrorException exception) when (exception.Code == "CLOSING_RULE_REQUIRED")
        {
            return null;
        }
    }

    private async Task CompleteAsync(CancellationToken cancellationToken)
    {
        var result = await initialSetup.CompleteAsync(cancellationToken).ConfigureAwait(false);
        sessionState.InitialSetupState = result;
        ApplyIssues(result.Issues);
        CanComplete = result.Status == InitialSetupStatus.Completed && result.Issues.Count == 0;
        if (!CanComplete)
        {
            await RefreshConfirmationAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var period = await payrollPeriods.FindPeriodAsync(localToday, cancellationToken).ConfigureAwait(false);
        sessionState.PayrollPeriod = period.Key;
        sessionState.SelectedRootRoute = NavigationRoutes.Home;
        await rootNavigator.SetRootAsync(new AppRootNavigationRequest(AppRootKind.Main, null), cancellationToken)
            .ConfigureAwait(false);
    }

    private void ApplyIssues(IReadOnlyList<IssueDto> issues)
    {
        MissingRequirements = issues.Count == 0
            ? null
            : string.Join(Environment.NewLine, issues.Select(issue => $"・{issue.Message}"));
    }

    internal static PayrollPeriodKey GetCurrentPayrollPeriodKey(DateOnly localToday, int? closingDay)
    {
        var month = new YearMonth(localToday.Year, localToday.Month);
        if (closingDay is null) return new PayrollPeriodKey(month);
        return new PayrollPeriodKey(localToday.Day > closingDay.Value ? month.AddMonths(1) : month);
    }
}

public sealed class WelcomeStepViewModel
{
    public string Description => "勤務内容と設定値から給与見込み額を計算します。正式な給与明細の代わりにはなりません。";

    public string StorageNotice => "データはこの端末内だけに保存されます。端末の故障やアンインストールに備えて、設定後は定期的にエクスポートしてください。";
}

public sealed record ClosingDayOption(string DisplayName, int? Value);

public sealed class ClosingDayStepViewModel : ObservableObject
{
    private ClosingDayOption? selectedOption;
    private string periodPreview = "締め日を選択すると、最初の給与算定期間を表示します。";

    public ClosingDayStepViewModel()
    {
        ClosingDayOptions = [new ClosingDayOption("月末", null),
            .. Enumerable.Range(1, 31).Select(day => new ClosingDayOption($"{day}日", day))];
    }

    public IReadOnlyList<ClosingDayOption> ClosingDayOptions { get; }

    public ClosingDayOption? SelectedOption
    {
        get => selectedOption;
        set
        {
            if (!SetProperty(ref selectedOption, value)) return;
            if (PreviewCommand is AsyncCommand command) command.NotifyCanExecuteChanged();
        }
    }

    public ICommand? PreviewCommand { get; set; }

    public string PeriodPreview
    {
        get => periodPreview;
        private set => SetProperty(ref periodPreview, value);
    }

    public void Select(int? closingDay) => SelectedOption = ClosingDayOptions.Single(option => option.Value == closingDay);

    public void SetPreview(PayrollPeriod period, JapaneseDisplayFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(formatter);
        PeriodPreview = $"最初の給与期間: {period.Key.Value.Year}年{period.Key.Value.Month}月分\n{formatter.PayrollPeriod(period)}";
    }
}

public sealed record RateTypeOption(string DisplayName, RateType Value)
{
    public static IReadOnlyList<RateTypeOption> All { get; } =
    [
        new("時給", RateType.Hourly),
        new("時間区分ごとの固定額", RateType.FixedPerRecord),
    ];
}

public sealed class SetupRateEditorViewModel : ObservableObject
{
    private string displayName;
    private string standardMinutesText;
    private RateTypeOption selectedRateType;
    private string amountText;
    private bool isEnabled;

    public SetupRateEditorViewModel(TimeCategoryId id, string displayName, int standardMinutes,
        RateType rateType, long? amount, bool isEnabled, ServicePresetId? presetId)
    {
        Id = id;
        this.displayName = displayName;
        standardMinutesText = standardMinutes.ToString(CultureInfo.InvariantCulture);
        selectedRateType = RateTypeOptions.Single(value => value.Value == rateType);
        amountText = amount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        this.isEnabled = isEnabled;
        PresetId = presetId;
    }

    public TimeCategoryId Id { get; }

    public ServicePresetId? PresetId { get; }

    public IReadOnlyList<RateTypeOption> RateTypeOptions => RateTypeOption.All;

    public string DisplayName
    {
        get => displayName;
        set => SetProperty(ref displayName, value);
    }

    public string StandardMinutesText
    {
        get => standardMinutesText;
        set => SetProperty(ref standardMinutesText, value);
    }

    public RateTypeOption SelectedRateType
    {
        get => selectedRateType;
        set => SetProperty(ref selectedRateType, value ?? RateTypeOption.All[0]);
    }

    public string AmountText
    {
        get => amountText;
        set => SetProperty(ref amountText, value);
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }
}

public sealed class SetupServiceEditorViewModel : ObservableObject
{
    private string displayName;
    private bool isEnabled;

    public SetupServiceEditorViewModel(ServiceId id, string displayName, bool isEnabled,
        IEnumerable<SetupRateEditorViewModel> rates)
    {
        Id = id;
        this.displayName = displayName;
        this.isEnabled = isEnabled;
        Rates = new ObservableCollection<SetupRateEditorViewModel>(rates);
    }

    public ServiceId Id { get; }

    public ObservableCollection<SetupRateEditorViewModel> Rates { get; }

    public string DisplayName
    {
        get => displayName;
        set => SetProperty(ref displayName, value);
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }
}

public sealed class ServiceRatesStepViewModel : ObservableObject
{
    private readonly AsyncCommand addServiceCommand;

    public ServiceRatesStepViewModel()
    {
        addServiceCommand = new AsyncCommand(() =>
        {
            AddService();
            return Task.CompletedTask;
        }, _ => { });
    }

    public ObservableCollection<SetupServiceEditorViewModel> Services { get; } = [];

    public ICommand AddServiceCommand => addServiceCommand;

    public void Load(SettingSnapshot snapshot, IReadOnlyList<ServicePresetDto> presets)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(presets);
        Services.Clear();
        var presetsByCategory = presets.Where(value => value.TimeCategoryId is not null)
            .GroupBy(value => value.TimeCategoryId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(value => value.DisplayOrder.Value).First());

        foreach (var service in snapshot.Services.OrderBy(value => value.DisplayOrder.Value))
        {
            var rates = snapshot.TimeCategories.Where(value => value.ServiceId == service.Id)
                .OrderBy(value => value.DisplayOrder.Value)
                .Select(category =>
                {
                    var rate = snapshot.Rates.FirstOrDefault(value => value.ServiceId == service.Id && value.TimeCategoryId == category.Id)
                        ?? snapshot.Rates.FirstOrDefault(value => value.ServiceId == service.Id && value.TimeCategoryId is null);
                    presetsByCategory.TryGetValue(category.Id, out var preset);
                    return new SetupRateEditorViewModel(category.Id, preset?.DisplayName ?? category.DisplayName,
                        category.StandardMinutes.Value, rate?.RateType ?? RateType.Hourly, rate?.Amount.Value,
                        category.IsEnabled, preset?.Id);
                });
            Services.Add(new SetupServiceEditorViewModel(service.Id, service.DisplayName, service.IsEnabled, rates));
        }
    }

    public SettingSnapshotReplacementDto BuildReplacement(SettingSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (Services.Count == 0)
            throw new ApplicationErrorException("SETUP_SERVICE_REQUIRED", "少なくとも1つのサービス設定を追加してください。");

        var serviceNames = new HashSet<string>(StringComparer.Ordinal);
        var services = new List<SnapshotService>();
        var categories = new List<SnapshotTimeCategory>();
        var rates = new List<SnapshotRate>();
        var enabledRateCount = 0;

        for (var serviceIndex = 0; serviceIndex < Services.Count; serviceIndex++)
        {
            var editor = Services[serviceIndex];
            var serviceName = RequiredText(editor.DisplayName, "サービス種類名を入力してください。");
            if (!serviceNames.Add(serviceName))
                throw new ApplicationErrorException("SETUP_SERVICE_NAME_DUPLICATE", "サービス種類名が重複しています。");
            if (editor.Rates.Count == 0)
                throw new ApplicationErrorException("SETUP_RATE_ROW_REQUIRED", $"「{serviceName}」にサービス設定を追加してください。");

            var serviceEnabled = editor.IsEnabled && editor.Rates.Any(value => value.IsEnabled);
            services.Add(new SnapshotService(editor.Id, serviceName, new DisplayOrder(serviceIndex), serviceEnabled));
            var categoryNames = new HashSet<string>(StringComparer.Ordinal);
            for (var categoryIndex = 0; categoryIndex < editor.Rates.Count; categoryIndex++)
            {
                var row = editor.Rates[categoryIndex];
                var categoryName = RequiredText(row.DisplayName, "サービス設定名を入力してください。");
                if (!categoryNames.Add(categoryName))
                    throw new ApplicationErrorException("SETUP_CATEGORY_NAME_DUPLICATE", $"「{serviceName}」のサービス設定名が重複しています。");
                if (!int.TryParse(row.StandardMinutesText, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes is < 1 or > 1440)
                    throw new ApplicationErrorException("SETUP_MINUTES_INVALID", $"「{categoryName}」の標準勤務時間を1～1440分で入力してください。");

                var categoryEnabled = serviceEnabled && row.IsEnabled;
                categories.Add(new SnapshotTimeCategory(row.Id, editor.Id, categoryName, new WorkMinutes(minutes),
                    new DisplayOrder(categoryIndex), categoryEnabled));
                if (TryAmount(row.AmountText, out var amount))
                    rates.Add(new SnapshotRate(editor.Id, row.Id, row.SelectedRateType.Value, new YenAmount(amount)));
                else if (categoryEnabled)
                    throw new ApplicationErrorException("SETUP_RATE_REQUIRED", $"「{categoryName}」の基本単価を0円以上の整数で入力してください。");
                if (categoryEnabled) enabledRateCount++;
            }
        }

        if (enabledRateCount == 0)
            throw new ApplicationErrorException("SETUP_ENABLED_SERVICE_REQUIRED", "使用するサービスを少なくとも1つ選び、基本単価を入力してください。");
        return new SettingSnapshotReplacementDto(services, categories, rates, current.Premiums, current.CountBonuses);
    }

    public async Task SavePresetsAsync(IServicePresetUseCase presetUseCase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(presetUseCase);
        var displayOrder = 0;
        foreach (var service in Services)
        foreach (var row in service.Rates)
        {
            if (!int.TryParse(row.StandardMinutesText, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
                continue;
            await presetUseCase.SaveAsync(new SaveServicePresetCommand(row.PresetId,
                RequiredText(row.DisplayName, "サービス設定名を入力してください。"), service.Id, row.Id,
                new WorkMinutes(minutes), new DisplayOrder(displayOrder++), service.IsEnabled && row.IsEnabled), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private void AddService()
    {
        var number = Services.Count + 1;
        Services.Add(new SetupServiceEditorViewModel(new ServiceId(Guid.NewGuid()), $"新しいサービス{number}", true,
        [
            new SetupRateEditorViewModel(new TimeCategoryId(Guid.NewGuid()), $"新しい設定{number}", 60,
                RateType.Hourly, null, true, null),
        ]));
    }

    private static string RequiredText(string? value, string message)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ApplicationErrorException("SETUP_TEXT_REQUIRED", message);
        return normalized;
    }

    private static bool TryAmount(string? value, out long amount) =>
        long.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out amount) && amount >= 0;
}

public sealed record AdditionCalculationTypeOption(string DisplayName, PremiumCalculationType Value)
{
    public static IReadOnlyList<AdditionCalculationTypeOption> All { get; } =
    [
        new("時間当たり固定額", PremiumCalculationType.FixedPerHour),
        new("割合", PremiumCalculationType.Percentage),
        new("1件当たり固定額", PremiumCalculationType.FixedPerRecord),
    ];
}

public sealed class PremiumSetupEditorViewModel : ObservableObject
{
    private bool isEnabled;
    private AdditionCalculationTypeOption selectedCalculationType = AdditionCalculationTypeOption.All[0];
    private string valueText = string.Empty;
    private string startTimeText = string.Empty;
    private string endTimeText = string.Empty;

    public PremiumSetupEditorViewModel(string displayName, bool requiresTimeRange)
    {
        DisplayName = displayName;
        RequiresTimeRange = requiresTimeRange;
    }

    public string DisplayName { get; }

    public bool RequiresTimeRange { get; }

    public IReadOnlyList<AdditionCalculationTypeOption> CalculationTypeOptions => AdditionCalculationTypeOption.All;

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    public AdditionCalculationTypeOption SelectedCalculationType
    {
        get => selectedCalculationType;
        set
        {
            if (!SetProperty(ref selectedCalculationType, value ?? AdditionCalculationTypeOption.All[0])) return;
            OnPropertyChanged(nameof(ValueLabel));
        }
    }

    public string ValueLabel => SelectedCalculationType.Value == PremiumCalculationType.Percentage ? "割合（%）" : "加算額（円）";

    public string ValueText
    {
        get => valueText;
        set => SetProperty(ref valueText, value);
    }

    public string StartTimeText
    {
        get => startTimeText;
        set => SetProperty(ref startTimeText, value);
    }

    public string EndTimeText
    {
        get => endTimeText;
        set => SetProperty(ref endTimeText, value);
    }
}

public sealed class CountBonusSetupEditorViewModel : ObservableObject
{
    private bool isEnabled;
    private string amountText = string.Empty;

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    public string AmountText
    {
        get => amountText;
        set => SetProperty(ref amountText, value);
    }
}

public sealed class AdditionsStepViewModel
{
    private SnapshotPremium? holidaySource;
    private SnapshotPremium? nightSource;
    private SnapshotCountBonus? countSource;

    public AdditionsStepViewModel()
    {
        Holiday = new PremiumSetupEditorViewModel("休日", false);
        Night = new PremiumSetupEditorViewModel("夜間", true);
        CountBonus = new CountBonusSetupEditorViewModel();
    }

    public PremiumSetupEditorViewModel Holiday { get; }

    public PremiumSetupEditorViewModel Night { get; }

    public CountBonusSetupEditorViewModel CountBonus { get; }

    public ICommand? SkipCommand { get; set; }

    public void Load(SettingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        holidaySource = snapshot.Premiums.FirstOrDefault(value => value.DisplayName == "休日");
        nightSource = snapshot.Premiums.FirstOrDefault(value => value.DisplayName == "夜間");
        countSource = snapshot.CountBonuses.FirstOrDefault(value => value.DisplayName == "件数加算")
            ?? snapshot.CountBonuses.FirstOrDefault();
        LoadPremium(Holiday, holidaySource, "", "");
        LoadPremium(Night, nightSource, "22:00", "05:00");
        CountBonus.IsEnabled = countSource?.IsEnabled ?? false;
        CountBonus.AmountText = countSource is { Amount.Value: > 0 }
            ? countSource.Amount.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    public void DisableAll()
    {
        Holiday.IsEnabled = false;
        Night.IsEnabled = false;
        CountBonus.IsEnabled = false;
    }

    public SettingSnapshotReplacementDto BuildReplacement(SettingSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var premiums = current.Premiums.Where(value => value.Id != holidaySource?.Id && value.Id != nightSource?.Id).ToList();
        premiums.Add(BuildPremium(Holiday, holidaySource, isHoliday: true));
        premiums.Add(BuildPremium(Night, nightSource, isHoliday: false));

        var bonuses = current.CountBonuses.Where(value => value.Id != countSource?.Id).ToList();
        bonuses.Add(BuildCountBonus());
        return new SettingSnapshotReplacementDto(current.Services, current.TimeCategories, current.Rates, premiums, bonuses);
    }

    private static void LoadPremium(PremiumSetupEditorViewModel target, SnapshotPremium? source,
        string defaultStart, string defaultEnd)
    {
        target.IsEnabled = source?.IsEnabled ?? false;
        target.SelectedCalculationType = AdditionCalculationTypeOption.All.Single(option =>
            option.Value == (source?.CalculationType ?? PremiumCalculationType.FixedPerHour));
        target.ValueText = source switch
        {
            { CalculationType: PremiumCalculationType.Percentage, Percentage.Value: > 0 } =>
                (source.Percentage.Value.Value / 100m).ToString("0.##", CultureInfo.InvariantCulture),
            { CalculationType: not PremiumCalculationType.Percentage, Amount.Value: > 0 } =>
                source.Amount.Value.Value.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty,
        };
        target.StartTimeText = source?.StartTime is { } start ? FormatTime(start) : defaultStart;
        target.EndTimeText = source?.EndTime is { } end ? FormatTime(end) : defaultEnd;
    }

    private static SnapshotPremium BuildPremium(PremiumSetupEditorViewModel editor, SnapshotPremium? source, bool isHoliday)
    {
        var (percentage, amount) = ParsePremiumValue(editor);
        MinuteOfDay? start = null;
        MinuteOfDay? end = null;
        if (editor.RequiresTimeRange)
        {
            start = ParseTime(editor.StartTimeText, "夜間の開始時刻をHH:mm形式で入力してください。", editor.IsEnabled);
            end = ParseTime(editor.EndTimeText, "夜間の終了時刻をHH:mm形式で入力してください。", editor.IsEnabled);
            if (start == end && start is not null)
                throw new ApplicationErrorException("SETUP_PREMIUM_TIME_INVALID", "夜間の開始時刻と終了時刻は異なる時刻にしてください。");
        }

        return new SnapshotPremium(source?.Id ?? new PremiumId(Guid.NewGuid()), editor.DisplayName,
            editor.SelectedCalculationType.Value, percentage, amount, start, end,
            source?.UsesNationalHolidays ?? isHoliday,
            source?.Weekdays ?? (isHoliday ? new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday } : new HashSet<DayOfWeek>()),
            source?.Dates ?? new HashSet<DateOnly>(), source?.ServiceIds ?? new HashSet<ServiceId>(), editor.IsEnabled);
    }

    private SnapshotCountBonus BuildCountBonus()
    {
        long amount = 0;
        if (CountBonus.IsEnabled && (!long.TryParse(CountBonus.AmountText?.Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out amount) || amount < 0))
            throw new ApplicationErrorException("SETUP_COUNT_AMOUNT_INVALID", "件数加算額を0円以上の整数で入力してください。");
        if (!CountBonus.IsEnabled && countSource is not null) amount = countSource.Amount.Value;
        return new SnapshotCountBonus(countSource?.Id ?? new CountBonusId(Guid.NewGuid()),
            countSource?.DisplayName ?? "件数加算", new YenAmount(amount),
            countSource?.ServiceIds ?? new HashSet<ServiceId>(), CountBonus.IsEnabled);
    }

    private static (BasisPoints? Percentage, YenAmount? Amount) ParsePremiumValue(PremiumSetupEditorViewModel editor)
    {
        if (!editor.IsEnabled)
        {
            return editor.SelectedCalculationType.Value == PremiumCalculationType.Percentage
                ? (new BasisPoints(0), null)
                : (null, new YenAmount(0));
        }

        if (editor.SelectedCalculationType.Value == PremiumCalculationType.Percentage)
        {
            if (!decimal.TryParse(editor.ValueText?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var percent) || percent < 0)
                throw new ApplicationErrorException("SETUP_PREMIUM_PERCENTAGE_INVALID", $"{editor.DisplayName}の割合を0%以上で入力してください。");
            var basisPoints = decimal.ToInt32(decimal.Round(percent * 100m, 0, MidpointRounding.AwayFromZero));
            return (new BasisPoints(basisPoints), null);
        }

        if (!long.TryParse(editor.ValueText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount < 0)
            throw new ApplicationErrorException("SETUP_PREMIUM_AMOUNT_INVALID", $"{editor.DisplayName}の加算額を0円以上の整数で入力してください。");
        return (null, new YenAmount(amount));
    }

    private static MinuteOfDay? ParseTime(string? value, string message, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) throw new ApplicationErrorException("SETUP_PREMIUM_TIME_REQUIRED", message);
            return null;
        }
        if (!TimeOnly.TryParseExact(value.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
            throw new ApplicationErrorException("SETUP_PREMIUM_TIME_INVALID", message);
        return new MinuteOfDay(time.Hour * 60 + time.Minute);
    }

    private static string FormatTime(MinuteOfDay value) => $"{value.Value / 60:00}:{value.Value % 60:00}";
}

public sealed class SetupConfirmationStepViewModel : ObservableObject
{
    private string closingSummary = string.Empty;
    private string serviceSummary = string.Empty;
    private string additionsSummary = string.Empty;
    private string? missingRequirements;

    public string ClosingSummary
    {
        get => closingSummary;
        private set => SetProperty(ref closingSummary, value);
    }

    public string ServiceSummary
    {
        get => serviceSummary;
        private set => SetProperty(ref serviceSummary, value);
    }

    public string AdditionsSummary
    {
        get => additionsSummary;
        private set => SetProperty(ref additionsSummary, value);
    }

    public string? MissingRequirements
    {
        get => missingRequirements;
        private set
        {
            if (!SetProperty(ref missingRequirements, value)) return;
            OnPropertyChanged(nameof(HasMissingRequirements));
        }
    }

    public bool HasMissingRequirements => !string.IsNullOrWhiteSpace(MissingRequirements);

    public void Update(string closing, string services, string additions, string? issues)
    {
        ClosingSummary = closing;
        ServiceSummary = services;
        AdditionsSummary = additions;
        MissingRequirements = issues;
    }
}
