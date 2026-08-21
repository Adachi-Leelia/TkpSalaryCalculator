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

public static class SetupFieldIds
{
    public const string ClosingDay = "Setup.ClosingDay";
    public const string Services = "Setup.Services";
    public const string Additions = "Setup.Additions";
    public const string HolidayValue = "Setup.Additions.Holiday.Value";
    public const string NightValue = "Setup.Additions.Night.Value";
    public const string NightStartTime = "Setup.Additions.Night.StartTime";
    public const string NightEndTime = "Setup.Additions.Night.EndTime";
    public const string CountBonusAmount = "Setup.Additions.CountBonus.Amount";

    public static string ForErrorCode(string code) => code switch
    {
        "SETUP_CLOSING_DAY_REQUIRED" => ClosingDay,
        "SETUP_PREMIUM_PERCENTAGE_INVALID" or "SETUP_PREMIUM_AMOUNT_INVALID" => Additions,
        "SETUP_PREMIUM_TIME_REQUIRED" or "SETUP_PREMIUM_TIME_INVALID" => NightStartTime,
        "SETUP_COUNT_AMOUNT_INVALID" => CountBonusAmount,
        _ when code.StartsWith("SETUP_SERVICE", StringComparison.Ordinal) ||
            code.StartsWith("SETUP_RATE", StringComparison.Ordinal) ||
            code.StartsWith("SETUP_CATEGORY", StringComparison.Ordinal) ||
            code.StartsWith("SETUP_MINUTES", StringComparison.Ordinal) ||
            code.StartsWith("SETUP_ENABLED_SERVICE", StringComparison.Ordinal) => Services,
        _ => Additions,
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
    private readonly AsyncCommand fixClosingDayCommand;
    private readonly AsyncCommand fixServicesCommand;
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
        fixClosingDayCommand = new AsyncCommand(
            GoToClosingDayAsync, PresentError);
        fixServicesCommand = new AsyncCommand(
            GoToServicesAsync, PresentError);
        Confirmation.FixClosingDayCommand = fixClosingDayCommand;
        Confirmation.FixServicesCommand = fixServicesCommand;
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

    public string? FirstErrorField { get; private set; }

    public event EventHandler<string>? ErrorFocusRequested;

    public Task InitializeAsync()
    {
        if (initialized) return Task.CompletedTask;
        return RunBusyAsync(InitializeCoreAsync);
    }

    public Task MoveNextAsync()
    {
        ClearValidationErrors();
        return RunBusyAsync(MoveNextCoreAsync);
    }

    public Task MoveBackAsync()
    {
        ClearValidationErrors();
        return RunBusyAsync(MoveBackCoreAsync);
    }

    public Task SkipAdditionsAsync() => RunBusyAsync(async cancellationToken =>
    {
        ClearValidationErrors();
        if (CurrentStep != InitialSetupStep.Additions) return;
        Additions.DisableAll();
        await SaveAdditionsAsync(cancellationToken);
        await MoveToAsync(InitialSetupStep.Confirmation, cancellationToken);
        await RefreshConfirmationAsync(cancellationToken);
    });

    public Task PreviewClosingDayAsync() => RunBusyAsync(async cancellationToken =>
    {
        ClearValidationErrors();
        if (ClosingDay.SelectedOption is null) return;
        var preview = await GetClosingDayPreviewAsync(cancellationToken);
        ClosingDay.SetPreview(preview.ReplacementPeriod, formatter);
    });

    public Task GoToClosingDayAsync() => MoveToStepAsync(InitialSetupStep.ClosingDay);

    public Task GoToServicesAsync() => MoveToStepAsync(InitialSetupStep.Services);

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        var state = await initialSetup.GetStateAsync(cancellationToken);
        sessionState.InitialSetupState = state;
        CurrentStep = InitialSetupStepIds.ToStep(state.Step);
        ResumeMessage = state.Status == InitialSetupStatus.InProgress && !string.IsNullOrWhiteSpace(state.Step)
            ? $"保存済みの「{StepTitle}」から再開しました。"
            : null;

        var settings = await monthSettings.GetAsync(setupMonth, cancellationToken);
        var presetValues = await presets.GetAllAsync(cancellationToken);
        Services.Load(settings.Snapshot, presetValues);
        Additions.Load(settings.Snapshot);
        await LoadClosingDayAsync(cancellationToken);
        if (CurrentStep == InitialSetupStep.Confirmation)
            await RefreshConfirmationAsync(cancellationToken);
        else
            ApplyIssues(state.Issues);
        initialized = true;
    }

    private async Task LoadClosingDayAsync(CancellationToken cancellationToken)
    {
        var lookupKey = new PayrollPeriodKey(setupMonth);
        var existing = await payrollPeriods.GetClosingRuleAsync(lookupKey, cancellationToken);
        if (existing is null) return;

        ClosingDay.Select(existing.ClosingDay);
        var period = await payrollPeriods.FindPeriodAsync(localToday, cancellationToken);
        ClosingDay.SetPreview(period, formatter);
        sessionState.PayrollPeriod = period.Key;
    }

    private async Task MoveNextCoreAsync(CancellationToken cancellationToken)
    {
        switch (CurrentStep)
        {
            case InitialSetupStep.Welcome:
                await MoveToAsync(InitialSetupStep.ClosingDay, cancellationToken);
                break;
            case InitialSetupStep.ClosingDay:
                await SaveClosingDayAsync(cancellationToken);
                await MoveToAsync(InitialSetupStep.Services, cancellationToken);
                break;
            case InitialSetupStep.Services:
                await SaveServicesAsync(cancellationToken);
                await MoveToAsync(InitialSetupStep.Additions, cancellationToken);
                break;
            case InitialSetupStep.Additions:
                await SaveAdditionsAsync(cancellationToken);
                await MoveToAsync(InitialSetupStep.Confirmation, cancellationToken);
                await RefreshConfirmationAsync(cancellationToken);
                break;
            case InitialSetupStep.Confirmation:
                await CompleteAsync(cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task MoveBackCoreAsync(CancellationToken cancellationToken)
    {
        if (!CanGoBack) return;
        var destination = (InitialSetupStep)((int)CurrentStep - 1);
        await MoveToAsync(destination, cancellationToken);
    }

    private async Task MoveToAsync(InitialSetupStep step, CancellationToken cancellationToken)
    {
        var id = InitialSetupStepIds.FromStep(step);
        await initialSetup.SaveProgressAsync(id, cancellationToken);
        var state = new InitialSetupStateDto(InitialSetupStatus.InProgress, id,
            sessionState.InitialSetupState?.Issues ?? []);
        sessionState.InitialSetupState = state;
        CurrentStep = step;
        if (step != InitialSetupStep.Confirmation) CanComplete = false;
    }

    private async Task SaveClosingDayAsync(CancellationToken cancellationToken)
    {
        if (ClosingDay.SelectedOption is null)
            throw new ApplicationErrorException("SETUP_CLOSING_DAY_REQUIRED", "締め日を選択してください。", SetupFieldIds.ClosingDay);

        var closingDay = ClosingDay.SelectedOption.Value;
        var key = GetCurrentPayrollPeriodKey(localToday, closingDay);
        var command = new ReplaceClosingRuleCommand(key, closingDay);
        var preview = await GetClosingDayPreviewAsync(cancellationToken);
        ClosingDay.SetPreview(preview.ReplacementPeriod, formatter);
        await payrollPeriods.ReplaceClosingRuleAsync(command, preview.ConfirmationToken, cancellationToken);
        sessionState.PayrollPeriod = preview.ReplacementPeriod.Key;
    }

    private Task<ClosingRuleReplacementPreviewDto> GetClosingDayPreviewAsync(CancellationToken cancellationToken)
    {
        var closingDay = ClosingDay.SelectedOption?.Value;
        if (ClosingDay.SelectedOption is null)
            throw new ApplicationErrorException("SETUP_CLOSING_DAY_REQUIRED", "締め日を選択してください。", SetupFieldIds.ClosingDay);
        var key = GetCurrentPayrollPeriodKey(localToday, closingDay);
        return payrollPeriods.PreviewClosingRuleReplacementAsync(
            new ReplaceClosingRuleCommand(key, closingDay), cancellationToken);
    }

    private async Task SaveServicesAsync(CancellationToken cancellationToken)
    {
        var current = await monthSettings.GetAsync(setupMonth, cancellationToken);
        var replacement = Services.BuildReplacement(current.Snapshot);
        var preview = await monthSettings.PreviewReplacementAsync(setupMonth, replacement, cancellationToken);
        if (preview.Issues.Count != 0)
            throw ToApplicationError(preview.Issues[0], SetupFieldIds.Services);

        await monthSettings.CloneAndReplaceAsync(setupMonth, replacement, preview.ConfirmationToken, cancellationToken);
        await Services.SavePresetsAsync(presets, cancellationToken);
    }

    private async Task SaveAdditionsAsync(CancellationToken cancellationToken)
    {
        var current = await monthSettings.GetAsync(setupMonth, cancellationToken);
        var replacement = Additions.BuildReplacement(current.Snapshot);
        var preview = await monthSettings.PreviewReplacementAsync(setupMonth, replacement, cancellationToken);
        if (preview.Issues.Count != 0)
            throw ToApplicationError(preview.Issues[0], SetupFieldIds.Additions);

        await monthSettings.CloneAndReplaceAsync(setupMonth, replacement, preview.ConfirmationToken, cancellationToken);
    }

    private async Task RefreshConfirmationAsync(CancellationToken cancellationToken)
    {
        var state = await initialSetup.GetStateAsync(cancellationToken);
        var settings = await monthSettings.GetAsync(setupMonth, cancellationToken);
        sessionState.InitialSetupState = state;
        ApplyIssues(state.Issues);
        CanComplete = state.Issues.Count == 0;

        var period = await TryFindCurrentPeriodAsync(cancellationToken);
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

        Confirmation.Update(ClosingSummary, ServiceSummary, AdditionsSummary, state.Issues);
    }

    private async Task<PayrollPeriod?> TryFindCurrentPeriodAsync(CancellationToken cancellationToken)
    {
        try
        {
            var period = await payrollPeriods.FindPeriodAsync(localToday, cancellationToken);
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
        var result = await initialSetup.CompleteAsync(cancellationToken);
        sessionState.InitialSetupState = result;
        ApplyIssues(result.Issues);
        CanComplete = result.Status == InitialSetupStatus.Completed && result.Issues.Count == 0;
        if (!CanComplete)
        {
            await RefreshConfirmationAsync(cancellationToken);
            return;
        }

        var period = await payrollPeriods.FindPeriodAsync(localToday, cancellationToken);
        sessionState.PayrollPeriod = period.Key;
        sessionState.SelectedRootRoute = NavigationRoutes.Home;
        await rootNavigator.SetRootAsync(new AppRootNavigationRequest(AppRootKind.Main, null), cancellationToken);
    }

    private void ApplyIssues(IReadOnlyList<IssueDto> issues)
    {
        MissingRequirements = issues.Count == 0
            ? null
            : string.Join(Environment.NewLine, issues.Select(issue => $"・{issue.Message}"));
        Confirmation.SetIssues(issues);
    }

    private Task MoveToStepAsync(InitialSetupStep step)
    {
        ClearValidationErrors();
        return RunBusyAsync(token => MoveToAsync(step, token));
    }

    protected override void OnErrorPresented(Exception exception)
    {
        if (exception is not ApplicationErrorException applicationError) return;
        FirstErrorField = applicationError.Field ?? SetupFieldIds.ForErrorCode(applicationError.Code);
        OnPropertyChanged(nameof(FirstErrorField));
        ClosingDay.SetValidationError(FirstErrorField, applicationError.Message);
        Services.SetValidationError(FirstErrorField, applicationError.Message);
        Additions.SetValidationError(FirstErrorField, applicationError.Message);
        ErrorFocusRequested?.Invoke(this, FirstErrorField);
    }

    private void ClearValidationErrors()
    {
        FirstErrorField = null;
        OnPropertyChanged(nameof(FirstErrorField));
        ClosingDay.ClearValidationError();
        Services.ClearValidationErrors();
        Additions.ClearValidationErrors();
    }

    private static ApplicationErrorException ToApplicationError(IssueDto issue, string fallbackField) =>
        new(issue.Code, issue.Message, issue.Field ?? fallbackField);

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
    private string? validationError;

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

    public string FieldId => SetupFieldIds.ClosingDay;

    public string? ValidationError
    {
        get => validationError;
        private set
        {
            if (!SetProperty(ref validationError, value)) return;
            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);

    public void Select(int? closingDay) => SelectedOption = ClosingDayOptions.Single(option => option.Value == closingDay);

    public void SetPreview(PayrollPeriod period, JapaneseDisplayFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(formatter);
        PeriodPreview = $"最初の給与期間: {period.Key.Value.Year}年{period.Key.Value.Month}月分\n{formatter.PayrollPeriod(period)}";
    }

    public void SetValidationError(string? field, string message) =>
        ValidationError = field == FieldId ? message : null;

    public void ClearValidationError() => ValidationError = null;
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
    private bool canMoveUp;
    private bool canMoveDown;
    private string? validationError;
    private Action? moveUp;
    private Action? moveDown;
    private readonly AsyncCommand moveUpCommand;
    private readonly AsyncCommand moveDownCommand;

    public SetupRateEditorViewModel(TimeCategoryId id, string displayName, int standardMinutes,
        RateType rateType, long? amount, bool isEnabled, ServicePresetId? presetId)
    {
        Id = id;
        this.displayName = displayName;
        standardMinutesText = standardMinutes.ToString(CultureInfo.InvariantCulture);
        selectedRateType = RateTypeOptions.Single(value => value.Value == rateType);
        amountText = amount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        this.isEnabled = isEnabled;
        PresetId = presetId ?? new ServicePresetId(Guid.NewGuid());
        moveUpCommand = new AsyncCommand(() =>
        {
            moveUp?.Invoke();
            return Task.CompletedTask;
        }, _ => { }, () => canMoveUp);
        moveDownCommand = new AsyncCommand(() =>
        {
            moveDown?.Invoke();
            return Task.CompletedTask;
        }, _ => { }, () => canMoveDown);
    }

    public TimeCategoryId Id { get; }

    public ServicePresetId PresetId { get; private set; }

    public string DisplayNameFieldId => $"Setup.Rate.{Id.Value:N}.DisplayName";

    public string StandardMinutesFieldId => $"Setup.Rate.{Id.Value:N}.StandardMinutes";

    public string AmountFieldId => $"Setup.Rate.{Id.Value:N}.Amount";

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

    public string? ValidationError
    {
        get => validationError;
        private set
        {
            if (!SetProperty(ref validationError, value)) return;
            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);

    public ICommand MoveUpCommand => moveUpCommand;

    public ICommand MoveDownCommand => moveDownCommand;

    public void SetPresetId(ServicePresetId presetId) => PresetId = presetId;

    public void ConfigureMovement(Action onMoveUp, Action onMoveDown, bool moveUpEnabled, bool moveDownEnabled)
    {
        moveUp = onMoveUp;
        moveDown = onMoveDown;
        canMoveUp = moveUpEnabled;
        canMoveDown = moveDownEnabled;
        moveUpCommand.NotifyCanExecuteChanged();
        moveDownCommand.NotifyCanExecuteChanged();
    }

    public void SetValidationError(string? field, string message) => ValidationError =
        field == DisplayNameFieldId || field == StandardMinutesFieldId || field == AmountFieldId ? message : null;

    public void ClearValidationError() => ValidationError = null;
}

public sealed class SetupServiceEditorViewModel : ObservableObject
{
    private string displayName;
    private bool isEnabled;
    private bool isExpanded;
    private bool canMoveUp;
    private bool canMoveDown;
    private string? validationError;
    private Action? addRate;
    private Action? moveUp;
    private Action? moveDown;
    private readonly AsyncCommand addRateCommand;
    private readonly AsyncCommand toggleExpandedCommand;
    private readonly AsyncCommand moveUpCommand;
    private readonly AsyncCommand moveDownCommand;

    public SetupServiceEditorViewModel(ServiceId id, string displayName, bool isEnabled,
        IEnumerable<SetupRateEditorViewModel> rates)
    {
        Id = id;
        this.displayName = displayName;
        this.isEnabled = isEnabled;
        isExpanded = isEnabled;
        Rates = new ObservableCollection<SetupRateEditorViewModel>(rates);
        addRateCommand = new AsyncCommand(() =>
        {
            addRate?.Invoke();
            return Task.CompletedTask;
        }, _ => { });
        toggleExpandedCommand = new AsyncCommand(() =>
        {
            IsExpanded = !IsExpanded;
            return Task.CompletedTask;
        }, _ => { });
        moveUpCommand = new AsyncCommand(() =>
        {
            moveUp?.Invoke();
            return Task.CompletedTask;
        }, _ => { }, () => canMoveUp);
        moveDownCommand = new AsyncCommand(() =>
        {
            moveDown?.Invoke();
            return Task.CompletedTask;
        }, _ => { }, () => canMoveDown);
    }

    public ServiceId Id { get; }

    public ObservableCollection<SetupRateEditorViewModel> Rates { get; }

    public string DisplayNameFieldId => $"Setup.Service.{Id.Value:N}.DisplayName";

    public string AddRateFieldId => $"Setup.Service.{Id.Value:N}.AddRate";

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

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (!SetProperty(ref isExpanded, value)) return;
            OnPropertyChanged(nameof(ExpansionActionText));
        }
    }

    public string ExpansionActionText => IsExpanded ? "設定を折りたたむ" : "設定を展開する";

    public string? ValidationError
    {
        get => validationError;
        private set
        {
            if (!SetProperty(ref validationError, value)) return;
            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);

    public ICommand AddRateCommand => addRateCommand;

    public ICommand ToggleExpandedCommand => toggleExpandedCommand;

    public ICommand MoveUpCommand => moveUpCommand;

    public ICommand MoveDownCommand => moveDownCommand;

    public void ConfigureCommands(Action onAddRate, Action onMoveUp, Action onMoveDown,
        bool moveUpEnabled, bool moveDownEnabled)
    {
        addRate = onAddRate;
        moveUp = onMoveUp;
        moveDown = onMoveDown;
        canMoveUp = moveUpEnabled;
        canMoveDown = moveDownEnabled;
        moveUpCommand.NotifyCanExecuteChanged();
        moveDownCommand.NotifyCanExecuteChanged();
    }

    public void SetValidationError(string? field, string message) =>
        ValidationError = field == DisplayNameFieldId || field == AddRateFieldId ? message : null;

    public void ClearValidationError() => ValidationError = null;
}

public sealed class ServiceRatesStepViewModel : ObservableObject
{
    private readonly AsyncCommand addServiceCommand;
    private readonly AsyncCommand disableUnusedCommand;

    public ServiceRatesStepViewModel()
    {
        addServiceCommand = new AsyncCommand(() =>
        {
            AddService();
            return Task.CompletedTask;
        }, _ => { });
        disableUnusedCommand = new AsyncCommand(() =>
        {
            DisableUnusedCandidates();
            return Task.CompletedTask;
        }, _ => { });
    }

    public ObservableCollection<SetupServiceEditorViewModel> Services { get; } = [];

    public ICommand AddServiceCommand => addServiceCommand;

    public ICommand DisableUnusedCommand => disableUnusedCommand;

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
        WireCommands();
    }

    public SettingSnapshotReplacementDto BuildReplacement(SettingSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (Services.Count == 0)
            throw new ApplicationErrorException("SETUP_SERVICE_REQUIRED", "少なくとも1つのサービス設定を追加してください。",
                SetupFieldIds.Services);

        var serviceNames = new HashSet<string>(StringComparer.Ordinal);
        var services = new List<SnapshotService>();
        var categories = new List<SnapshotTimeCategory>();
        var rates = new List<SnapshotRate>();
        var enabledRateCount = 0;

        for (var serviceIndex = 0; serviceIndex < Services.Count; serviceIndex++)
        {
            var editor = Services[serviceIndex];
            var serviceName = RequiredText(editor.DisplayName, "サービス種類名を入力してください。", editor.DisplayNameFieldId);
            if (!serviceNames.Add(serviceName))
                throw new ApplicationErrorException("SETUP_SERVICE_NAME_DUPLICATE", "サービス種類名が重複しています。",
                    editor.DisplayNameFieldId);
            if (editor.Rates.Count == 0)
                throw new ApplicationErrorException("SETUP_RATE_ROW_REQUIRED", $"「{serviceName}」にサービス設定を追加してください。",
                    editor.AddRateFieldId);

            var serviceEnabled = editor.IsEnabled && editor.Rates.Any(value => value.IsEnabled);
            services.Add(new SnapshotService(editor.Id, serviceName, new DisplayOrder(serviceIndex), serviceEnabled));
            var categoryNames = new HashSet<string>(StringComparer.Ordinal);
            for (var categoryIndex = 0; categoryIndex < editor.Rates.Count; categoryIndex++)
            {
                var row = editor.Rates[categoryIndex];
                var categoryName = RequiredText(row.DisplayName, "サービス設定名を入力してください。", row.DisplayNameFieldId);
                if (!categoryNames.Add(categoryName))
                    throw new ApplicationErrorException("SETUP_CATEGORY_NAME_DUPLICATE", $"「{serviceName}」のサービス設定名が重複しています。",
                        row.DisplayNameFieldId);
                if (!int.TryParse(row.StandardMinutesText, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes is < 1 or > 1440)
                    throw new ApplicationErrorException("SETUP_MINUTES_INVALID", $"「{categoryName}」の標準勤務時間を1～1440分で入力してください。",
                        row.StandardMinutesFieldId);

                var categoryEnabled = serviceEnabled && row.IsEnabled;
                categories.Add(new SnapshotTimeCategory(row.Id, editor.Id, categoryName, new WorkMinutes(minutes),
                    new DisplayOrder(categoryIndex), categoryEnabled));
                if (TryAmount(row.AmountText, out var amount))
                    rates.Add(new SnapshotRate(editor.Id, row.Id, row.SelectedRateType.Value, new YenAmount(amount)));
                else if (categoryEnabled)
                    throw new ApplicationErrorException("SETUP_RATE_REQUIRED", $"「{categoryName}」の基本単価を0円以上の整数で入力してください。",
                        row.AmountFieldId);
                if (categoryEnabled) enabledRateCount++;
            }
        }

        if (enabledRateCount == 0)
            throw new ApplicationErrorException("SETUP_ENABLED_SERVICE_REQUIRED", "使用するサービスを少なくとも1つ選び、基本単価を入力してください。",
                SetupFieldIds.Services);
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
            var saved = await presetUseCase.SaveAsync(new SaveServicePresetCommand(row.PresetId,
                RequiredText(row.DisplayName, "サービス設定名を入力してください。", row.DisplayNameFieldId), service.Id, row.Id,
                new WorkMinutes(minutes), new DisplayOrder(displayOrder++), service.IsEnabled && row.IsEnabled), cancellationToken);
            row.SetPresetId(saved.Id);
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
        WireCommands();
    }

    private void AddRate(SetupServiceEditorViewModel service)
    {
        var number = service.Rates.Count + 1;
        service.Rates.Add(new SetupRateEditorViewModel(new TimeCategoryId(Guid.NewGuid()),
            $"新しい設定{number}", 60, RateType.Hourly, null, true, null));
        service.IsEnabled = true;
        service.IsExpanded = true;
        WireCommands();
    }

    private void MoveService(SetupServiceEditorViewModel service, int offset)
    {
        var current = Services.IndexOf(service);
        var destination = current + offset;
        if (current < 0 || destination < 0 || destination >= Services.Count) return;
        Services.Move(current, destination);
        WireCommands();
    }

    private void MoveRate(SetupServiceEditorViewModel service, SetupRateEditorViewModel rate, int offset)
    {
        var current = service.Rates.IndexOf(rate);
        var destination = current + offset;
        if (current < 0 || destination < 0 || destination >= service.Rates.Count) return;
        service.Rates.Move(current, destination);
        WireCommands();
    }

    private void WireCommands()
    {
        for (var serviceIndex = 0; serviceIndex < Services.Count; serviceIndex++)
        {
            var service = Services[serviceIndex];
            service.ConfigureCommands(() => AddRate(service), () => MoveService(service, -1),
                () => MoveService(service, 1), serviceIndex > 0, serviceIndex < Services.Count - 1);
            for (var rateIndex = 0; rateIndex < service.Rates.Count; rateIndex++)
            {
                var rate = service.Rates[rateIndex];
                rate.ConfigureMovement(() => MoveRate(service, rate, -1), () => MoveRate(service, rate, 1),
                    rateIndex > 0, rateIndex < service.Rates.Count - 1);
            }
        }
    }

    private void DisableUnusedCandidates()
    {
        var hasCalculableRow = Services.SelectMany(service => service.Rates)
            .Any(row => row.IsEnabled && TryAmount(row.AmountText, out _));
        var keptFallback = false;
        foreach (var service in Services)
        {
            foreach (var row in service.Rates.Where(row => row.IsEnabled && !TryAmount(row.AmountText, out _)))
            {
                if (!hasCalculableRow && !keptFallback)
                {
                    keptFallback = true;
                    continue;
                }
                row.IsEnabled = false;
            }
            service.IsEnabled = service.Rates.Any(row => row.IsEnabled);
            if (!service.IsEnabled) service.IsExpanded = false;
        }
    }

    public void SetValidationError(string? field, string message)
    {
        foreach (var service in Services)
        {
            service.SetValidationError(field, message);
            foreach (var rate in service.Rates) rate.SetValidationError(field, message);
            if (service.HasValidationError || service.Rates.Any(rate => rate.HasValidationError))
                service.IsExpanded = true;
        }
    }

    public void ClearValidationErrors()
    {
        foreach (var service in Services)
        {
            service.ClearValidationError();
            foreach (var rate in service.Rates) rate.ClearValidationError();
        }
    }

    private static string RequiredText(string? value, string message, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ApplicationErrorException("SETUP_TEXT_REQUIRED", message, field);
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
    private TimeSpan startTime = new(22, 0, 0);
    private TimeSpan endTime = new(5, 0, 0);
    private string? validationError;

    public PremiumSetupEditorViewModel(string displayName, bool requiresTimeRange)
    {
        DisplayName = displayName;
        RequiresTimeRange = requiresTimeRange;
    }

    public string DisplayName { get; }

    public bool RequiresTimeRange { get; }

    public string ValueFieldId => RequiresTimeRange ? SetupFieldIds.NightValue : SetupFieldIds.HolidayValue;

    public string StartTimeFieldId => SetupFieldIds.NightStartTime;

    public string EndTimeFieldId => SetupFieldIds.NightEndTime;

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

    public TimeSpan StartTime
    {
        get => startTime;
        set => SetProperty(ref startTime, value);
    }

    public TimeSpan EndTime
    {
        get => endTime;
        set => SetProperty(ref endTime, value);
    }

    public string? ValidationError
    {
        get => validationError;
        private set
        {
            if (!SetProperty(ref validationError, value)) return;
            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);

    public void SetValidationError(string? field, string message) => ValidationError =
        field == ValueFieldId || RequiresTimeRange && (field == StartTimeFieldId || field == EndTimeFieldId)
            ? message
            : null;

    public void ClearValidationError() => ValidationError = null;
}

public sealed class CountBonusSetupEditorViewModel : ObservableObject
{
    private bool isEnabled;
    private string amountText = string.Empty;
    private string? validationError;

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

    public string AmountFieldId => SetupFieldIds.CountBonusAmount;

    public string? ValidationError
    {
        get => validationError;
        private set
        {
            if (!SetProperty(ref validationError, value)) return;
            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);

    public void SetValidationError(string? field, string message) =>
        ValidationError = field == AmountFieldId ? message : null;

    public void ClearValidationError() => ValidationError = null;
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
        LoadPremium(Holiday, holidaySource, TimeSpan.Zero, TimeSpan.Zero);
        LoadPremium(Night, nightSource, new TimeSpan(22, 0, 0), new TimeSpan(5, 0, 0));
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

    public void SetValidationError(string? field, string message)
    {
        Holiday.SetValidationError(field, message);
        Night.SetValidationError(field, message);
        CountBonus.SetValidationError(field, message);
    }

    public void ClearValidationErrors()
    {
        Holiday.ClearValidationError();
        Night.ClearValidationError();
        CountBonus.ClearValidationError();
    }

    private static void LoadPremium(PremiumSetupEditorViewModel target, SnapshotPremium? source,
        TimeSpan defaultStart, TimeSpan defaultEnd)
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
        target.StartTime = source?.StartTime is { } start ? ToTimeSpan(start) : defaultStart;
        target.EndTime = source?.EndTime is { } end ? ToTimeSpan(end) : defaultEnd;
    }

    private static SnapshotPremium BuildPremium(PremiumSetupEditorViewModel editor, SnapshotPremium? source, bool isHoliday)
    {
        var (percentage, amount) = ParsePremiumValue(editor);
        MinuteOfDay? start = null;
        MinuteOfDay? end = null;
        if (editor.RequiresTimeRange)
        {
            start = editor.IsEnabled ? ToMinuteOfDay(editor.StartTime) : null;
            end = editor.IsEnabled ? ToMinuteOfDay(editor.EndTime) : null;
            if (start == end && start is not null)
                throw new ApplicationErrorException("SETUP_PREMIUM_TIME_INVALID", "夜間の開始時刻と終了時刻は異なる時刻にしてください。",
                    editor.EndTimeFieldId);
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
            throw new ApplicationErrorException("SETUP_COUNT_AMOUNT_INVALID", "件数加算額を0円以上の整数で入力してください。",
                CountBonus.AmountFieldId);
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
                throw new ApplicationErrorException("SETUP_PREMIUM_PERCENTAGE_INVALID", $"{editor.DisplayName}の割合を0%以上で入力してください。",
                    editor.ValueFieldId);
            var basisPoints = decimal.ToInt32(decimal.Round(percent * 100m, 0, MidpointRounding.AwayFromZero));
            return (new BasisPoints(basisPoints), null);
        }

        if (!long.TryParse(editor.ValueText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount < 0)
            throw new ApplicationErrorException("SETUP_PREMIUM_AMOUNT_INVALID", $"{editor.DisplayName}の加算額を0円以上の整数で入力してください。",
                editor.ValueFieldId);
        return (null, new YenAmount(amount));
    }

    private static MinuteOfDay ToMinuteOfDay(TimeSpan value) =>
        new((int)value.TotalMinutes);

    private static TimeSpan ToTimeSpan(MinuteOfDay value) => TimeSpan.FromMinutes(value.Value);
}

public sealed class SetupConfirmationStepViewModel : ObservableObject
{
    private string closingSummary = string.Empty;
    private string serviceSummary = string.Empty;
    private string additionsSummary = string.Empty;
    private string? missingRequirements;
    private bool hasClosingDayIssue;
    private bool hasServicesIssue;

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

    public bool HasClosingDayIssue
    {
        get => hasClosingDayIssue;
        private set => SetProperty(ref hasClosingDayIssue, value);
    }

    public bool HasServicesIssue
    {
        get => hasServicesIssue;
        private set => SetProperty(ref hasServicesIssue, value);
    }

    public ICommand? FixClosingDayCommand { get; set; }

    public ICommand? FixServicesCommand { get; set; }

    public void Update(string closing, string services, string additions, IReadOnlyList<IssueDto> issues)
    {
        ClosingSummary = closing;
        ServiceSummary = services;
        AdditionsSummary = additions;
        SetIssues(issues);
    }

    public void SetIssues(IReadOnlyList<IssueDto> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        MissingRequirements = issues.Count == 0
            ? null
            : string.Join(Environment.NewLine, issues.Select(issue => $"・{issue.Message}"));
        HasClosingDayIssue = issues.Any(issue => issue.Code == "SETUP_CLOSING_RULE_REQUIRED");
        HasServicesIssue = issues.Any(issue => issue.Code is "SETUP_SNAPSHOT_REQUIRED" or
            "SETUP_CALCULATION_SETTINGS_REQUIRED");
    }
}
