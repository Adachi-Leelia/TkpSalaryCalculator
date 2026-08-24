using System.Globalization;
using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

public sealed record ServiceSettingRow(
    Guid EditorId,
    string ServiceName,
    string TimeCategoryName,
    string StandardTimeText,
    string RateText,
    string StatusText,
    bool CanAddTimeCategory);

public sealed record ServicePresetRow(
    Guid EditorId,
    string DisplayName,
    string ServiceName,
    string TimeCategoryName,
    string StandardTimeText,
    string StatusText);

/// <summary>SCR-SERVICE-01 の全期間共通候補と対象年月設定を別セクションで表示します。</summary>
public sealed class ServiceSettingsViewModel : ViewModelBase
{
    private readonly SettingsMonthContext context;
    private readonly IServicePresetUseCase presets;
    private readonly ISettingsNavigator navigator;
    private readonly JapaneseDisplayFormatter formatter;
    private IReadOnlyList<ServiceSettingRow> monthlyRows = [];
    private IReadOnlyList<ServicePresetRow> inputCandidateRows = [];
    private string? successMessage;

    public ServiceSettingsViewModel(
        SettingsMonthContext context,
        IServicePresetUseCase presets,
        ISettingsNavigator navigator,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.presets = presets ?? throw new ArgumentNullException(nameof(presets));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        TrackDataChanges(context.SessionState, AppDataChangeKind.Settings);
        AddServiceCommand = new AsyncCommand(
            () => navigator.OpenServiceEditorAsync(ServiceSettingsEditorMode.AddService, null, default), PresentError);
    }

    public string MonthHeaderText => context.HeaderText;
    public string MonthlySectionTitle => $"{formatter.Month(context.SelectedMonth)}の給与設定";
    public IReadOnlyList<ServiceSettingRow> MonthlyRows { get => monthlyRows; private set { if (SetProperty(ref monthlyRows, value)) { OnPropertyChanged(nameof(HasMonthlyRows)); OnPropertyChanged(nameof(HasNoMonthlyRows)); } } }
    public IReadOnlyList<ServicePresetRow> InputCandidateRows { get => inputCandidateRows; private set { if (SetProperty(ref inputCandidateRows, value)) { OnPropertyChanged(nameof(HasInputCandidates)); OnPropertyChanged(nameof(HasNoInputCandidates)); } } }
    public bool HasMonthlyRows => MonthlyRows.Count != 0;
    public bool HasNoMonthlyRows => !HasMonthlyRows;
    public bool HasInputCandidates => InputCandidateRows.Count != 0;
    public bool HasNoInputCandidates => !HasInputCandidates;
    public AsyncCommand AddServiceCommand { get; }
    public string? SuccessMessage { get => successMessage; private set { if (SetProperty(ref successMessage, value)) OnPropertyChanged(nameof(HasSuccessMessage)); } }
    public bool HasSuccessMessage => !string.IsNullOrWhiteSpace(SuccessMessage);

    public Task LoadAsync() => LoadTrackedAsync(LoadCoreAsync, force: true);
    public Task LoadIfNeededAsync() => LoadTrackedAsync(LoadCoreAsync, force: false);

    private async Task LoadCoreAsync(CancellationToken token)
    {
        var monthTask = context.RefreshAsync(token);
        var presetsTask = presets.GetAllAsync(token);
        await Task.WhenAll(monthTask, presetsTask);
        var snapshot = monthTask.Result.Snapshot;
        MonthlyRows = BuildMonthlyRows(snapshot);
        InputCandidateRows = BuildPresetRows(snapshot, presetsTask.Result);
        OnPropertyChanged(nameof(MonthHeaderText));
        OnPropertyChanged(nameof(MonthlySectionTitle));
    }

    public Task OpenEditorAsync(ServiceSettingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return navigator.OpenServiceEditorAsync(ServiceSettingsEditorMode.EditMonthlySetting, row.EditorId, default);
    }

    public Task OpenCandidateEditorAsync(ServicePresetRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return navigator.OpenServiceEditorAsync(ServiceSettingsEditorMode.EditInputCandidate, row.EditorId, default);
    }

    public Task AddTimeCategoryAsync(ServiceSettingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.CanAddTimeCategory) throw new ArgumentException("時間区分の追加先としてサービス種類を指定してください。", nameof(row));
        return navigator.OpenServiceEditorAsync(ServiceSettingsEditorMode.AddTimeCategory, row.EditorId, default);
    }

    public void SetSuccessMessage(string? value) => SuccessMessage = value;

    private IReadOnlyList<ServiceSettingRow> BuildMonthlyRows(SettingSnapshot snapshot)
    {
        var rows = new List<ServiceSettingRow>();
        foreach (var service in snapshot.Services.OrderBy(value => value.DisplayOrder.Value))
        {
            var categories = snapshot.TimeCategories.Where(value => value.ServiceId == service.Id)
                .OrderBy(value => value.DisplayOrder.Value).ToArray();
            if (categories.Length != 0)
            {
                var serviceRate = snapshot.Rates.FirstOrDefault(value => value.ServiceId == service.Id && value.TimeCategoryId is null);
                rows.Add(new(service.Id.Value, service.DisplayName, "任意時間", "サービス種類単位",
                    RateText(serviceRate), service.IsEnabled ? "有効" : "この年月では無効", true));
            }
            if (categories.Length == 0)
            {
                var rate = snapshot.Rates.FirstOrDefault(value => value.ServiceId == service.Id && value.TimeCategoryId is null);
                rows.Add(new(service.Id.Value, service.DisplayName, "任意時間", "勤務時に入力",
                    RateText(rate), service.IsEnabled ? "有効" : "この年月では無効", true));
                continue;
            }
            foreach (var category in categories)
            {
                var rate = snapshot.Rates.FirstOrDefault(value => value.ServiceId == service.Id && value.TimeCategoryId == category.Id)
                    ?? snapshot.Rates.FirstOrDefault(value => value.ServiceId == service.Id && value.TimeCategoryId is null);
                rows.Add(new(category.Id.Value, service.DisplayName, category.DisplayName,
                    formatter.Duration(category.StandardMinutes), RateText(rate),
                    service.IsEnabled && category.IsEnabled ? "有効" : "この年月では無効", false));
            }
        }
        return rows;
    }

    private IReadOnlyList<ServicePresetRow> BuildPresetRows(SettingSnapshot snapshot, IReadOnlyList<ServicePresetDto> values) =>
        [.. values.OrderBy(value => value.DisplayOrder.Value).Select(value => new ServicePresetRow(
            value.Id.Value,
            value.DisplayName,
            snapshot.Services.FirstOrDefault(service => service.Id == value.ServiceId)?.DisplayName ?? "現在の年月では利用不可",
            value.TimeCategoryId is { } categoryId
                ? snapshot.TimeCategories.FirstOrDefault(category => category.Id == categoryId)?.DisplayName ?? "現在の年月では利用不可"
                : "任意時間",
            formatter.Duration(value.DefaultWorkMinutes),
            value.IsEnabled ? "候補として有効" : "候補として無効"))];

    private string RateText(SnapshotRate? rate) => rate is null
        ? "単価未設定"
        : $"{(rate.RateType == RateType.Hourly ? "時給" : "固定額")} {formatter.Money(rate.Amount)}";
}

/// <summary>SCR-SERVICE-02 の対象年月設定と全期間共通入力候補を編集します。</summary>
public sealed class ServiceSettingsEditorViewModel : MonthSettingsEditorViewModel
{
    private readonly IServicePresetUseCase presets;
    private ServiceSettingsEditorMode mode = ServiceSettingsEditorMode.AddService;
    private Guid? editorId;
    private SnapshotService? sourceService;
    private SnapshotTimeCategory? sourceCategory;
    private SnapshotRate? sourceRate;
    private ServicePresetDto? sourcePreset;
    private string serviceName = string.Empty;
    private string categoryName = string.Empty;
    private string categoryStandardMinutesText = "60";
    private string serviceDisplayOrderText = "0";
    private string categoryDisplayOrderText = "0";
    private bool useTimeCategory = true;
    private bool serviceIsEnabled = true;
    private bool timeCategoryIsEnabled = true;
    private RateTypeOption selectedRateType = RateTypeOption.All[0];
    private string amountText = string.Empty;
    private bool saveInputCandidate = true;
    private string candidateName = string.Empty;
    private string candidateDefaultMinutesText = "60";
    private string candidateOrderText = "0";
    private bool candidateEnabled = true;

    public ServiceSettingsEditorViewModel(
        SettingsMonthContext context,
        IMonthSettingsUseCase settings,
        IServicePresetUseCase presets,
        IConfirmationDialogService dialogs,
        JapaneseDisplayFormatter formatter,
        ISettingsNavigator navigator,
        IUserErrorPresenter errorPresenter) : base(context, settings, dialogs, formatter, navigator, errorPresenter)
    {
        this.presets = presets ?? throw new ArgumentNullException(nameof(presets));
        SaveCommand = new AsyncCommand(SaveAsync, PresentError);
    }

    public string PageTitle => mode switch
    {
        ServiceSettingsEditorMode.AddService => "サービス種類を追加",
        ServiceSettingsEditorMode.AddTimeCategory => "時間区分を追加",
        ServiceSettingsEditorMode.EditInputCandidate => "入力候補を編集",
        _ => "サービス・単価を編集",
    };
    public IReadOnlyList<RateTypeOption> RateTypes => RateTypeOption.All;
    public AsyncCommand SaveCommand { get; }
    public string ServiceName { get => serviceName; set { if (SetProperty(ref serviceName, value)) Changed(); } }
    public string CategoryName { get => categoryName; set { if (SetProperty(ref categoryName, value)) Changed(); } }
    public string CategoryStandardMinutesText { get => categoryStandardMinutesText; set { if (SetProperty(ref categoryStandardMinutesText, value)) Changed(); } }
    public string ServiceDisplayOrderText { get => serviceDisplayOrderText; set { if (SetProperty(ref serviceDisplayOrderText, value)) Changed(); } }
    public string CategoryDisplayOrderText { get => categoryDisplayOrderText; set { if (SetProperty(ref categoryDisplayOrderText, value)) Changed(); } }
    public bool UseTimeCategory
    {
        get => useTimeCategory;
        set
        {
            if (!SetProperty(ref useTimeCategory, value)) return;
            Changed();
            OnPropertyChanged(nameof(ShowTimeCategoryFields));
        }
    }
    public bool CanChooseTimeCategory => mode == ServiceSettingsEditorMode.AddService;
    public bool IsServiceReadOnly => mode == ServiceSettingsEditorMode.AddTimeCategory;
    public bool CanEditService => !IsServiceReadOnly;
    public bool ShowTimeCategoryFields => UseTimeCategory;
    public bool ServiceIsEnabled { get => serviceIsEnabled; set { if (SetProperty(ref serviceIsEnabled, value)) Changed(); } }
    public bool TimeCategoryIsEnabled { get => timeCategoryIsEnabled; set { if (SetProperty(ref timeCategoryIsEnabled, value)) Changed(); } }
    public RateTypeOption SelectedRateType { get => selectedRateType; set { if (SetProperty(ref selectedRateType, value ?? RateTypeOption.All[0])) Changed(); } }
    public string AmountText { get => amountText; set { if (SetProperty(ref amountText, value)) Changed(); } }
    public bool SaveInputCandidate { get => saveInputCandidate; set { if (SetProperty(ref saveInputCandidate, value)) Changed(); } }
    public string CandidateName { get => candidateName; set { if (SetProperty(ref candidateName, value)) Changed(); } }
    public string CandidateDefaultMinutesText { get => candidateDefaultMinutesText; set { if (SetProperty(ref candidateDefaultMinutesText, value)) Changed(); } }
    public string CandidateOrderText { get => candidateOrderText; set { if (SetProperty(ref candidateOrderText, value)) Changed(); } }
    public bool CandidateEnabled { get => candidateEnabled; set { if (SetProperty(ref candidateEnabled, value)) Changed(); } }

    public void Initialize(ServiceSettingsEditorMode editorMode, Guid? id)
    {
        if (editorMode != ServiceSettingsEditorMode.AddService && id is null)
            throw new ArgumentException("編集対象IDが必要です。", nameof(id));
        mode = editorMode;
        editorId = id;
        InvalidateTrackedLoad();
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(CanChooseTimeCategory));
        OnPropertyChanged(nameof(IsServiceReadOnly));
        OnPropertyChanged(nameof(CanEditService));
    }

    public Task LoadAsync() => LoadTrackedAsync(LoadCoreAsync, force: true);
    public Task LoadIfNeededAsync() => LoadTrackedAsync(LoadCoreAsync, force: false);

    private Task LoadCoreAsync(CancellationToken token) => LoadEditorAsync(async snapshot =>
    {
        var candidates = await presets.GetAllAsync(token);
        sourcePreset = mode == ServiceSettingsEditorMode.EditInputCandidate && editorId is { } presetId
            ? candidates.FirstOrDefault(value => value.Id.Value == presetId)
            : null;
        sourceCategory = mode switch
        {
            ServiceSettingsEditorMode.EditInputCandidate when sourcePreset?.TimeCategoryId is { } presetCategoryId =>
                snapshot.TimeCategories.FirstOrDefault(value => value.Id == presetCategoryId),
            ServiceSettingsEditorMode.EditMonthlySetting when editorId is { } id =>
                snapshot.TimeCategories.FirstOrDefault(value => value.Id.Value == id),
            _ => null,
        };
        if (mode == ServiceSettingsEditorMode.EditInputCandidate && sourcePreset is null)
            throw new ApplicationErrorException("SERVICE_PRESET_NOT_FOUND", "編集対象の入力候補が見つかりません。設定一覧から開き直してください。");
        if (mode == ServiceSettingsEditorMode.EditInputCandidate && sourcePreset?.TimeCategoryId is not null && sourceCategory is null)
            throw new ApplicationErrorException("SERVICE_PRESET_UNAVAILABLE_FOR_MONTH",
                "入力候補の時間区分は選択中の設定対象年月にありません。対象年月を変更してから開き直してください。");
        sourceService = mode switch
        {
            ServiceSettingsEditorMode.EditInputCandidate when sourcePreset is not null =>
                snapshot.Services.FirstOrDefault(value => value.Id == sourcePreset.ServiceId),
            ServiceSettingsEditorMode.AddTimeCategory when editorId is { } serviceId =>
                snapshot.Services.FirstOrDefault(value => value.Id.Value == serviceId),
            ServiceSettingsEditorMode.EditMonthlySetting when editorId is { } serviceOrCategoryId =>
                snapshot.Services.FirstOrDefault(value => value.Id.Value == serviceOrCategoryId || value.Id == sourceCategory?.ServiceId),
            _ => null,
        };
        if (mode is ServiceSettingsEditorMode.AddTimeCategory or ServiceSettingsEditorMode.EditMonthlySetting or ServiceSettingsEditorMode.EditInputCandidate &&
            sourceService is null)
            throw new ApplicationErrorException("SERVICE_SETTING_NOT_FOUND", "編集対象のサービス設定が見つかりません。設定一覧から開き直してください。");
        sourceRate = sourceService is null ? null : snapshot.Rates.FirstOrDefault(value =>
            value.ServiceId == sourceService.Id && value.TimeCategoryId == sourceCategory?.Id);

        serviceName = sourceService?.DisplayName ?? string.Empty;
        categoryName = sourceCategory?.DisplayName ?? string.Empty;
        categoryStandardMinutesText = (sourceCategory?.StandardMinutes.Value ?? 60).ToString(CultureInfo.InvariantCulture);
        serviceDisplayOrderText = (sourceService?.DisplayOrder.Value ?? snapshot.Services.Count).ToString(CultureInfo.InvariantCulture);
        categoryDisplayOrderText = (sourceCategory?.DisplayOrder.Value ??
            (sourceService is null ? 0 : snapshot.TimeCategories.Count(value => value.ServiceId == sourceService.Id)))
            .ToString(CultureInfo.InvariantCulture);
        useTimeCategory = mode is ServiceSettingsEditorMode.AddService or ServiceSettingsEditorMode.AddTimeCategory || sourceCategory is not null;
        serviceIsEnabled = sourceService?.IsEnabled ?? true;
        timeCategoryIsEnabled = sourceCategory?.IsEnabled ?? true;
        selectedRateType = RateTypeOption.All.Single(value => value.Value == (sourceRate?.RateType ?? RateType.Hourly));
        amountText = sourceRate?.Amount.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        saveInputCandidate = sourcePreset is not null || mode is ServiceSettingsEditorMode.AddService or ServiceSettingsEditorMode.AddTimeCategory;
        candidateName = sourcePreset?.DisplayName ?? (sourceService?.DisplayName ?? string.Empty);
        candidateDefaultMinutesText = (sourcePreset?.DefaultWorkMinutes.Value ?? sourceCategory?.StandardMinutes.Value ?? 60)
            .ToString(CultureInfo.InvariantCulture);
        candidateOrderText = (sourcePreset?.DisplayOrder.Value ?? candidates.Count).ToString(CultureInfo.InvariantCulture);
        candidateEnabled = sourcePreset?.IsEnabled ?? true;
        NotifyAll();
    }, token);

    public Task SaveAsync() => RunBusyAsync(async token =>
    {
        ResetFirstInvalidField();
        var built = BuildReplacement();
        await ConfirmAndSaveAsync(
            built.Replacement,
            "サービス・単価の変更を確認",
            "給与設定と入力候補の変更をまとめて保存します。",
            "サービス・単価を保存しました。",
            BuildPresetChange(built), token);
    });

    private (SettingSnapshotReplacementDto Replacement, ServiceId ServiceId, TimeCategoryId? CategoryId) BuildReplacement()
    {
        var name = ServiceName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ApplicationErrorException("SERVICE_NAME_REQUIRED", "サービス種類名を入力してください。", nameof(ServiceName));
        var category = CategoryName?.Trim();
        if (!UseTimeCategory) category = "任意時間";
        if (string.IsNullOrWhiteSpace(category)) throw new ApplicationErrorException("CATEGORY_NAME_REQUIRED", "時間区分名を入力してください。", nameof(CategoryName));
        var categoryMinutes = UseTimeCategory
            ? ParseMinutes(CategoryStandardMinutesText, nameof(CategoryStandardMinutesText), "時間区分の標準時間")
            : 60;
        var serviceOrder = ParseNonNegative(ServiceDisplayOrderText, nameof(ServiceDisplayOrderText), "サービス種類の表示順");
        var categoryOrder = UseTimeCategory
            ? ParseNonNegative(CategoryDisplayOrderText, nameof(CategoryDisplayOrderText), "時間区分の表示順")
            : 0;
        if (!long.TryParse(AmountText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount < 0)
            throw new ApplicationErrorException("RATE_AMOUNT_INVALID", "基本単価を0円以上の整数で入力してください。", nameof(AmountText));

        var serviceId = sourceService?.Id ?? new ServiceId(Guid.NewGuid());
        TimeCategoryId? categoryId = sourceCategory?.Id ?? (UseTimeCategory ? new TimeCategoryId(Guid.NewGuid()) : null);
        var services = Snapshot.Services.Where(value => value.Id != serviceId).ToList();
        services.Add(new SnapshotService(serviceId, name, new DisplayOrder(serviceOrder), ServiceIsEnabled));
        var categories = Snapshot.TimeCategories.Where(value => value.Id != categoryId).ToList();
        if (categoryId is { } id)
            categories.Add(new SnapshotTimeCategory(id, serviceId, category!, new WorkMinutes(categoryMinutes), new DisplayOrder(categoryOrder), TimeCategoryIsEnabled));
        var rates = Snapshot.Rates.Where(value => !(value.ServiceId == serviceId && value.TimeCategoryId == categoryId)).ToList();
        rates.Add(new SnapshotRate(serviceId, categoryId, SelectedRateType.Value, new YenAmount(amount)));
        return (new SettingSnapshotReplacementDto(services, categories, rates, Snapshot.Premiums, Snapshot.CountBonuses), serviceId, categoryId);
    }

    private ServicePresetChangeCommand BuildPresetChange(
        (SettingSnapshotReplacementDto Replacement, ServiceId ServiceId, TimeCategoryId? CategoryId) built)
    {
        if (!SaveInputCandidate)
            return new(null, sourcePreset?.Id);

        var name = CandidateName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ApplicationErrorException("PRESET_NAME_REQUIRED", "入力候補名を入力してください。", nameof(CandidateName));
        var order = ParseNonNegative(CandidateOrderText, nameof(CandidateOrderText), "入力候補の表示順");
        var minutes = ParseMinutes(CandidateDefaultMinutesText, nameof(CandidateDefaultMinutesText), "入力候補の標準勤務時間");
        return new(new SaveServicePresetCommand(sourcePreset?.Id, name, built.ServiceId, built.CategoryId,
            new WorkMinutes(minutes), new DisplayOrder(order), CandidateEnabled), null);
    }

    private static int ParseMinutes(string text, string field, string label)
    {
        if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 1 or > 1440)
            throw new ApplicationErrorException("STANDARD_MINUTES_INVALID", $"{label}を1分から1,440分で入力してください。", field);
        return value;
    }

    private static int ParseNonNegative(string text, string field, string label)
    {
        if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new ApplicationErrorException("DISPLAY_ORDER_INVALID", $"{label}を0以上の整数で入力してください。", field);
        return value;
    }

    private void NotifyAll()
    {
        foreach (var name in new[] { nameof(ServiceName), nameof(CategoryName), nameof(CategoryStandardMinutesText),
                     nameof(ServiceDisplayOrderText), nameof(CategoryDisplayOrderText), nameof(UseTimeCategory), nameof(CanChooseTimeCategory),
                     nameof(IsServiceReadOnly), nameof(CanEditService), nameof(ShowTimeCategoryFields), nameof(ServiceIsEnabled), nameof(TimeCategoryIsEnabled),
                     nameof(SelectedRateType), nameof(AmountText), nameof(SaveInputCandidate), nameof(CandidateName),
                     nameof(CandidateDefaultMinutesText), nameof(CandidateOrderText), nameof(CandidateEnabled) })
            OnPropertyChanged(name);
    }
}
