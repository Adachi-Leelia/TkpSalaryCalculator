using System.Globalization;
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
    string StatusText);

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
        AddCommand = new AsyncCommand(() => navigator.OpenServiceEditorAsync(null, default), PresentError);
    }

    public string MonthHeaderText => context.HeaderText;
    public string MonthlySectionTitle => $"{formatter.Month(context.SelectedMonth)}の給与設定";
    public IReadOnlyList<ServiceSettingRow> MonthlyRows { get => monthlyRows; private set { if (SetProperty(ref monthlyRows, value)) { OnPropertyChanged(nameof(HasMonthlyRows)); OnPropertyChanged(nameof(HasNoMonthlyRows)); } } }
    public IReadOnlyList<ServicePresetRow> InputCandidateRows { get => inputCandidateRows; private set { if (SetProperty(ref inputCandidateRows, value)) { OnPropertyChanged(nameof(HasInputCandidates)); OnPropertyChanged(nameof(HasNoInputCandidates)); } } }
    public bool HasMonthlyRows => MonthlyRows.Count != 0;
    public bool HasNoMonthlyRows => !HasMonthlyRows;
    public bool HasInputCandidates => InputCandidateRows.Count != 0;
    public bool HasNoInputCandidates => !HasInputCandidates;
    public AsyncCommand AddCommand { get; }
    public string? SuccessMessage { get => successMessage; private set { if (SetProperty(ref successMessage, value)) OnPropertyChanged(nameof(HasSuccessMessage)); } }
    public bool HasSuccessMessage => !string.IsNullOrWhiteSpace(SuccessMessage);

    public Task LoadAsync() => RunBusyAsync(async token =>
    {
        var monthTask = context.RefreshAsync(token);
        var presetsTask = presets.GetAllAsync(token);
        await Task.WhenAll(monthTask, presetsTask);
        var snapshot = monthTask.Result.Snapshot;
        MonthlyRows = BuildMonthlyRows(snapshot);
        InputCandidateRows = BuildPresetRows(snapshot, presetsTask.Result);
        OnPropertyChanged(nameof(MonthHeaderText));
        OnPropertyChanged(nameof(MonthlySectionTitle));
    });

    public Task OpenEditorAsync(ServiceSettingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return navigator.OpenServiceEditorAsync(row.EditorId, default);
    }

    public Task OpenCandidateEditorAsync(ServicePresetRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return navigator.OpenServiceEditorAsync(row.EditorId, default);
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
                    RateText(serviceRate), service.IsEnabled ? "有効" : "この年月では無効"));
            }
            if (categories.Length == 0)
            {
                var rate = snapshot.Rates.FirstOrDefault(value => value.ServiceId == service.Id && value.TimeCategoryId is null);
                rows.Add(new(service.Id.Value, service.DisplayName, "任意時間", "勤務時に入力",
                    RateText(rate), service.IsEnabled ? "有効" : "この年月では無効"));
                continue;
            }
            foreach (var category in categories)
            {
                var rate = snapshot.Rates.FirstOrDefault(value => value.ServiceId == service.Id && value.TimeCategoryId == category.Id)
                    ?? snapshot.Rates.FirstOrDefault(value => value.ServiceId == service.Id && value.TimeCategoryId is null);
                rows.Add(new(category.Id.Value, service.DisplayName, category.DisplayName,
                    formatter.Duration(category.StandardMinutes), RateText(rate),
                    service.IsEnabled && category.IsEnabled ? "有効" : "この年月では無効"));
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
    private Guid? editorId;
    private SnapshotService? sourceService;
    private SnapshotTimeCategory? sourceCategory;
    private SnapshotRate? sourceRate;
    private ServicePresetDto? sourcePreset;
    private string serviceName = string.Empty;
    private string categoryName = string.Empty;
    private string standardMinutesText = "60";
    private string displayOrderText = "0";
    private bool useTimeCategory = true;
    private bool serviceIsEnabled = true;
    private bool timeCategoryIsEnabled = true;
    private RateTypeOption selectedRateType = RateTypeOption.All[0];
    private string amountText = string.Empty;
    private bool saveInputCandidate = true;
    private string candidateName = string.Empty;
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

    public string PageTitle => editorId is null ? "サービス・単価を追加" : "サービス・単価を編集";
    public IReadOnlyList<RateTypeOption> RateTypes => RateTypeOption.All;
    public AsyncCommand SaveCommand { get; }
    public string ServiceName { get => serviceName; set { if (SetProperty(ref serviceName, value)) Changed(); } }
    public string CategoryName { get => categoryName; set { if (SetProperty(ref categoryName, value)) Changed(); } }
    public string StandardMinutesText { get => standardMinutesText; set { if (SetProperty(ref standardMinutesText, value)) Changed(); } }
    public string DisplayOrderText { get => displayOrderText; set { if (SetProperty(ref displayOrderText, value)) Changed(); } }
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
    public bool CanChooseTimeCategory => editorId is null;
    public bool ShowTimeCategoryFields => UseTimeCategory;
    public bool ServiceIsEnabled { get => serviceIsEnabled; set { if (SetProperty(ref serviceIsEnabled, value)) Changed(); } }
    public bool TimeCategoryIsEnabled { get => timeCategoryIsEnabled; set { if (SetProperty(ref timeCategoryIsEnabled, value)) Changed(); } }
    public RateTypeOption SelectedRateType { get => selectedRateType; set { if (SetProperty(ref selectedRateType, value ?? RateTypeOption.All[0])) Changed(); } }
    public string AmountText { get => amountText; set { if (SetProperty(ref amountText, value)) Changed(); } }
    public bool SaveInputCandidate { get => saveInputCandidate; set { if (SetProperty(ref saveInputCandidate, value)) Changed(); } }
    public string CandidateName { get => candidateName; set { if (SetProperty(ref candidateName, value)) Changed(); } }
    public string CandidateOrderText { get => candidateOrderText; set { if (SetProperty(ref candidateOrderText, value)) Changed(); } }
    public bool CandidateEnabled { get => candidateEnabled; set { if (SetProperty(ref candidateEnabled, value)) Changed(); } }

    public void Initialize(Guid? id) { editorId = id; OnPropertyChanged(nameof(PageTitle)); }

    public Task LoadAsync() => RunBusyAsync(token => LoadEditorAsync(async snapshot =>
    {
        var candidates = await presets.GetAllAsync(token);
        sourcePreset = editorId is { } presetId ? candidates.FirstOrDefault(value => value.Id.Value == presetId) : null;
        sourceCategory = sourcePreset?.TimeCategoryId is { } presetCategoryId
            ? snapshot.TimeCategories.FirstOrDefault(value => value.Id == presetCategoryId)
            : editorId is { } id ? snapshot.TimeCategories.FirstOrDefault(value => value.Id.Value == id) : null;
        sourceService = sourcePreset is not null
            ? snapshot.Services.FirstOrDefault(value => value.Id == sourcePreset.ServiceId)
            : editorId is { } serviceOrCategoryId
                ? snapshot.Services.FirstOrDefault(value => value.Id.Value == serviceOrCategoryId || value.Id == sourceCategory?.ServiceId)
                : null;
        sourceRate = sourceService is null ? null : snapshot.Rates.FirstOrDefault(value =>
            value.ServiceId == sourceService.Id && value.TimeCategoryId == sourceCategory?.Id);
        sourcePreset ??= sourceService is null ? null : candidates.FirstOrDefault(value =>
            value.ServiceId == sourceService.Id && value.TimeCategoryId == sourceCategory?.Id);

        serviceName = sourceService?.DisplayName ?? string.Empty;
        categoryName = sourceCategory?.DisplayName ?? string.Empty;
        standardMinutesText = (sourceCategory?.StandardMinutes.Value ?? 60).ToString(CultureInfo.InvariantCulture);
        displayOrderText = (sourceService?.DisplayOrder.Value ?? snapshot.Services.Count).ToString(CultureInfo.InvariantCulture);
        useTimeCategory = sourceCategory is not null || editorId is null;
        serviceIsEnabled = sourceService?.IsEnabled ?? true;
        timeCategoryIsEnabled = sourceCategory?.IsEnabled ?? true;
        selectedRateType = RateTypeOption.All.Single(value => value.Value == (sourceRate?.RateType ?? RateType.Hourly));
        amountText = sourceRate?.Amount.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        saveInputCandidate = sourcePreset is not null || editorId is null;
        candidateName = sourcePreset?.DisplayName ?? (sourceService?.DisplayName ?? string.Empty);
        candidateOrderText = (sourcePreset?.DisplayOrder.Value ?? candidates.Count).ToString(CultureInfo.InvariantCulture);
        candidateEnabled = sourcePreset?.IsEnabled ?? true;
        NotifyAll();
    }, token));

    public Task SaveAsync() => RunBusyAsync(async token =>
    {
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
        var minutes = UseTimeCategory ? ParseMinutes(StandardMinutesText) : 60;
        var order = ParseNonNegative(DisplayOrderText, nameof(DisplayOrderText), "表示順");
        if (!long.TryParse(AmountText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount < 0)
            throw new ApplicationErrorException("RATE_AMOUNT_INVALID", "基本単価を0円以上の整数で入力してください。", nameof(AmountText));

        var serviceId = sourceService?.Id ?? new ServiceId(Guid.NewGuid());
        TimeCategoryId? categoryId = sourceCategory?.Id ?? (UseTimeCategory ? new TimeCategoryId(Guid.NewGuid()) : null);
        var services = Snapshot.Services.Where(value => value.Id != serviceId).ToList();
        services.Add(new SnapshotService(serviceId, name, new DisplayOrder(order), ServiceIsEnabled));
        var categories = Snapshot.TimeCategories.Where(value => value.Id != categoryId).ToList();
        if (categoryId is { } id)
            categories.Add(new SnapshotTimeCategory(id, serviceId, category!, new WorkMinutes(minutes), new DisplayOrder(order), TimeCategoryIsEnabled));
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
        var minutes = ParseMinutes(StandardMinutesText);
        return new(new SaveServicePresetCommand(sourcePreset?.Id, name, built.ServiceId, built.CategoryId,
            new WorkMinutes(minutes), new DisplayOrder(order), CandidateEnabled), null);
    }

    private static int ParseMinutes(string text)
    {
        if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 1 or > 1440)
            throw new ApplicationErrorException("STANDARD_MINUTES_INVALID", "標準勤務時間を1分から1,440分で入力してください。", nameof(StandardMinutesText));
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
        foreach (var name in new[] { nameof(ServiceName), nameof(CategoryName), nameof(StandardMinutesText), nameof(DisplayOrderText),
                     nameof(UseTimeCategory), nameof(CanChooseTimeCategory), nameof(ShowTimeCategoryFields), nameof(ServiceIsEnabled), nameof(TimeCategoryIsEnabled),
                     nameof(SelectedRateType), nameof(AmountText), nameof(SaveInputCandidate), nameof(CandidateName),
                     nameof(CandidateOrderText), nameof(CandidateEnabled) })
            OnPropertyChanged(name);
    }
}
