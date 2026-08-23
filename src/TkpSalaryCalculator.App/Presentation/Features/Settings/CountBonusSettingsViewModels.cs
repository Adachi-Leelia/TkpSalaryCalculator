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

public sealed record CountBonusSettingRow(Guid Id, string DisplayName, string AmountText, string TargetText, string StatusText);

/// <summary>SCR-COUNT-01 の件数加算一覧です。</summary>
public sealed class CountBonusSettingsViewModel : ViewModelBase
{
    private readonly SettingsMonthContext context;
    private readonly ISettingsNavigator navigator;
    private readonly JapaneseDisplayFormatter formatter;
    private IReadOnlyList<CountBonusSettingRow> rows = [];
    private string? successMessage;

    public CountBonusSettingsViewModel(SettingsMonthContext context, ISettingsNavigator navigator,
        JapaneseDisplayFormatter formatter, IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        TrackDataChanges(context.SessionState, AppDataChangeKind.Settings);
        AddCommand = new AsyncCommand(() => navigator.OpenCountBonusEditorAsync(null, default), PresentError);
    }

    public string MonthHeaderText => context.HeaderText;
    public IReadOnlyList<CountBonusSettingRow> Rows { get => rows; private set { if (SetProperty(ref rows, value)) { OnPropertyChanged(nameof(HasRows)); OnPropertyChanged(nameof(HasNoRows)); } } }
    public bool HasRows => Rows.Count != 0;
    public bool HasNoRows => !HasRows;
    public AsyncCommand AddCommand { get; }
    public string? SuccessMessage { get => successMessage; private set { if (SetProperty(ref successMessage, value)) OnPropertyChanged(nameof(HasSuccessMessage)); } }
    public bool HasSuccessMessage => !string.IsNullOrWhiteSpace(SuccessMessage);

    public Task LoadAsync() => LoadTrackedAsync(LoadCoreAsync, force: true);
    public Task LoadIfNeededAsync() => LoadTrackedAsync(LoadCoreAsync, force: false);

    private async Task LoadCoreAsync(CancellationToken token)
    {
        var snapshot = (await context.RefreshAsync(token)).Snapshot;
        Rows = [.. snapshot.CountBonuses.Select(value => new CountBonusSettingRow(
            value.Id.Value, value.DisplayName, $"1件当たり {formatter.Money(value.Amount)}",
            value.ServiceIds.Count == 0 ? "全サービス対象" : string.Join("・", value.ServiceIds.Select(id =>
                snapshot.Services.FirstOrDefault(service => service.Id == id)?.DisplayName ?? "不明なサービス")),
            value.IsEnabled ? "有効" : "無効"))];
        OnPropertyChanged(nameof(MonthHeaderText));
    }

    public Task OpenEditorAsync(CountBonusSettingRow row) => navigator.OpenCountBonusEditorAsync(row.Id, default);
    public void SetSuccessMessage(string? value) => SuccessMessage = value;
}

/// <summary>SCR-COUNT-02 の1件加算条件を編集します。</summary>
public sealed class CountBonusSettingsEditorViewModel : MonthSettingsEditorViewModel
{
    private Guid? id;
    private SnapshotCountBonus? source;
    private string displayName = string.Empty;
    private string amountText = string.Empty;
    private bool appliesToAllServices = true;
    private bool isEnabled = true;
    private IReadOnlyList<SelectableServiceViewModel> services = [];

    public CountBonusSettingsEditorViewModel(SettingsMonthContext context, IMonthSettingsUseCase settings,
        IConfirmationDialogService dialogs, JapaneseDisplayFormatter formatter, ISettingsNavigator navigator,
        IUserErrorPresenter errorPresenter) : base(context, settings, dialogs, formatter, navigator, errorPresenter)
    {
        SaveCommand = new AsyncCommand(SaveAsync, PresentError);
    }

    public string PageTitle => id is null ? "件数加算を追加" : "件数加算を編集";
    public AsyncCommand SaveCommand { get; }
    public string DisplayName { get => displayName; set { if (SetProperty(ref displayName, value)) Changed(); } }
    public string AmountText { get => amountText; set { if (SetProperty(ref amountText, value)) Changed(); } }
    public bool AppliesToAllServices { get => appliesToAllServices; set { if (!SetProperty(ref appliesToAllServices, value)) return; Changed(); OnPropertyChanged(nameof(TargetExplanation)); OnPropertyChanged(nameof(ShowServiceSelection)); } }
    public bool ShowServiceSelection => !AppliesToAllServices;
    public bool IsEnabled { get => isEnabled; set { if (SetProperty(ref isEnabled, value)) Changed(); } }
    public IReadOnlyList<SelectableServiceViewModel> Services { get => services; private set => SetProperty(ref services, value); }
    public string TargetExplanation => AppliesToAllServices ? "対象サービスを指定していないため、全サービスへ適用します。" : "選択したサービスだけへ適用します。";

    public void Initialize(Guid? countBonusId) { id = countBonusId; InvalidateTrackedLoad(); OnPropertyChanged(nameof(PageTitle)); }

    public Task LoadAsync() => LoadTrackedAsync(LoadCoreAsync, force: true);
    public Task LoadIfNeededAsync() => LoadTrackedAsync(LoadCoreAsync, force: false);

    private Task LoadCoreAsync(CancellationToken token) => LoadEditorAsync(snapshot =>
    {
        source = id is { } bonusId ? snapshot.CountBonuses.FirstOrDefault(value => value.Id.Value == bonusId) : null;
        displayName = source?.DisplayName ?? string.Empty;
        amountText = source?.Amount.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        appliesToAllServices = source is null || source.ServiceIds.Count == 0;
        isEnabled = source?.IsEnabled ?? true;
        Services = [.. snapshot.Services.OrderBy(value => value.DisplayOrder.Value).Select(value =>
            new SelectableServiceViewModel(value.Id, value.DisplayName, source?.ServiceIds.Contains(value.Id) ?? false))];
        foreach (var service in Services) service.PropertyChanged += (_, _) => Changed();
        foreach (var name in new[] { nameof(DisplayName), nameof(AmountText), nameof(AppliesToAllServices), nameof(ShowServiceSelection), nameof(IsEnabled), nameof(TargetExplanation) })
            OnPropertyChanged(name);
        return Task.CompletedTask;
    }, token);

    public Task SaveAsync() => RunBusyAsync(async token =>
    {
        var replacement = BuildReplacement();
        await ConfirmAndSaveAsync(replacement, "件数加算の変更を確認",
            "件数加算の変更は選択中の設定対象年月だけに適用します。他の年月の給与設定は変更しません。",
            "件数加算設定を保存しました。", null, token);
    });

    private SettingSnapshotReplacementDto BuildReplacement()
    {
        var name = DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ApplicationErrorException("COUNT_NAME_REQUIRED", "表示名を入力してください。", nameof(DisplayName));
        if (!long.TryParse(AmountText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount < 0)
            throw new ApplicationErrorException("COUNT_AMOUNT_INVALID", "1件当たり加算額を0円以上の整数で入力してください。", nameof(AmountText));
        var serviceIds = AppliesToAllServices ? new HashSet<ServiceId>() : Services.Where(value => value.IsSelected).Select(value => value.Id).ToHashSet();
        if (!AppliesToAllServices && serviceIds.Count == 0)
            throw new ApplicationErrorException("COUNT_SERVICE_REQUIRED", "対象サービスを1つ以上選ぶか、全サービス対象を選択してください。", nameof(AppliesToAllServices));
        var bonus = new SnapshotCountBonus(source?.Id ?? new CountBonusId(Guid.NewGuid()), name, new YenAmount(amount), serviceIds, IsEnabled);
        var bonuses = Snapshot.CountBonuses.Where(value => value.Id != bonus.Id).Append(bonus).ToArray();
        return new(Snapshot.Services, Snapshot.TimeCategories, Snapshot.Rates, Snapshot.Premiums, bonuses);
    }
}
