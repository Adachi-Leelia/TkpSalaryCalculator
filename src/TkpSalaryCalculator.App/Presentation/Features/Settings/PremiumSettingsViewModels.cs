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

public sealed record PremiumSettingRow(Guid Id, string DisplayName, string CalculationText, string ConditionsText, string StatusText);

/// <summary>SCR-PREMIUM-01 の割増一覧です。</summary>
public sealed class PremiumSettingsViewModel : ViewModelBase
{
    private readonly SettingsMonthContext context;
    private readonly ISettingsNavigator navigator;
    private readonly JapaneseDisplayFormatter formatter;
    private IReadOnlyList<PremiumSettingRow> rows = [];
    private string? successMessage;

    public PremiumSettingsViewModel(SettingsMonthContext context, ISettingsNavigator navigator,
        JapaneseDisplayFormatter formatter, IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        TrackDataChanges(context.SessionState, AppDataChangeKind.Settings);
        AddCommand = new AsyncCommand(() => navigator.OpenPremiumEditorAsync(null, default), PresentError);
    }

    public string MonthHeaderText => context.HeaderText;
    public IReadOnlyList<PremiumSettingRow> Rows { get => rows; private set { if (SetProperty(ref rows, value)) { OnPropertyChanged(nameof(HasRows)); OnPropertyChanged(nameof(HasNoRows)); } } }
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
        Rows = [.. snapshot.Premiums.Select(value => new PremiumSettingRow(
            value.Id.Value, value.DisplayName, Calculation(value), Conditions(value, snapshot), value.IsEnabled ? "有効" : "無効"))];
        OnPropertyChanged(nameof(MonthHeaderText));
    }

    public Task OpenEditorAsync(PremiumSettingRow row) => navigator.OpenPremiumEditorAsync(row.Id, default);
    public void SetSuccessMessage(string? value) => SuccessMessage = value;

    private string Calculation(SnapshotPremium value) => value.CalculationType switch
    {
        PremiumCalculationType.Percentage => $"基本給与の {value.Percentage!.Value.Value / 100m:0.##}%",
        PremiumCalculationType.FixedPerHour => $"1時間当たり {formatter.Money(value.Amount!.Value)}",
        _ => $"1件当たり {formatter.Money(value.Amount!.Value)}",
    };

    private static string Conditions(SnapshotPremium value, SettingSnapshot snapshot)
    {
        var parts = new List<string>();
        if (value.Weekdays.Count > 0) parts.Add(string.Join("・", value.Weekdays.OrderBy(day => day).Select(JapaneseWeekday)));
        if (value.UsesNationalHolidays) parts.Add("国民の祝日");
        if (value.Dates.Count > 0) parts.Add($"個別日付 {value.Dates.Count}日");
        if (value.StartTime is { } start && value.EndTime is { } end) parts.Add($"{Time(start)}～{Time(end)}");
        if (value.ServiceIds.Count > 0)
            parts.Add(string.Join("・", value.ServiceIds.Select(id => snapshot.Services.FirstOrDefault(service => service.Id == id)?.DisplayName ?? "不明なサービス")));
        else parts.Add("全サービス");
        if (parts.Count == 1) parts.Insert(0, "すべての曜日・日付・時間帯");
        return string.Join(" / ", parts);
    }

    internal static string JapaneseWeekday(DayOfWeek value) => value switch
    {
        DayOfWeek.Sunday => "日曜", DayOfWeek.Monday => "月曜", DayOfWeek.Tuesday => "火曜",
        DayOfWeek.Wednesday => "水曜", DayOfWeek.Thursday => "木曜", DayOfWeek.Friday => "金曜", _ => "土曜",
    };
    internal static string Time(MinuteOfDay value) => $"{value.Value / 60:D2}:{value.Value % 60:D2}";
}

/// <summary>SCR-PREMIUM-02 の割増条件を編集します。</summary>
public sealed class PremiumSettingsEditorViewModel : MonthSettingsEditorViewModel
{
    private Guid? id;
    private SnapshotPremium? source;
    private string displayName = string.Empty;
    private PremiumTypeOption selectedCalculationType = PremiumTypeOption.All[1];
    private string valueText = string.Empty;
    private bool usesNationalHolidays;
    private bool sunday;
    private bool monday;
    private bool tuesday;
    private bool wednesday;
    private bool thursday;
    private bool friday;
    private bool saturday;
    private string individualDatesText = string.Empty;
    private bool usesTimeRange;
    private TimeSpan startTime = new(22, 0, 0);
    private TimeSpan endTime = new(5, 0, 0);
    private bool appliesToAllServices = true;
    private bool isEnabled = true;
    private IReadOnlyList<SelectableServiceViewModel> services = [];

    public PremiumSettingsEditorViewModel(SettingsMonthContext context, IMonthSettingsUseCase settings,
        IConfirmationDialogService dialogs, JapaneseDisplayFormatter formatter, ISettingsNavigator navigator,
        IUserErrorPresenter errorPresenter) : base(context, settings, dialogs, formatter, navigator, errorPresenter)
    {
        SaveCommand = new AsyncCommand(SaveAsync, PresentError);
    }

    public string PageTitle => id is null ? "割増を追加" : "割増を編集";
    public IReadOnlyList<PremiumTypeOption> CalculationTypes => PremiumTypeOption.All;
    public AsyncCommand SaveCommand { get; }
    public string DisplayName { get => displayName; set { if (SetProperty(ref displayName, value)) ChangedAndConditions(); } }
    public PremiumTypeOption SelectedCalculationType { get => selectedCalculationType; set { if (!SetProperty(ref selectedCalculationType, value ?? PremiumTypeOption.All[1])) return; Changed(); OnPropertyChanged(nameof(ValueLabel)); } }
    public string ValueLabel => SelectedCalculationType.Value == PremiumCalculationType.Percentage ? "割合（%）" : "加算額（円）";
    public string ValueText { get => valueText; set { if (SetProperty(ref valueText, value)) Changed(); } }
    public bool UsesNationalHolidays { get => usesNationalHolidays; set { if (SetProperty(ref usesNationalHolidays, value)) ChangedAndConditions(); } }
    public bool Sunday { get => sunday; set { if (SetProperty(ref sunday, value)) ChangedAndConditions(); } }
    public bool Monday { get => monday; set { if (SetProperty(ref monday, value)) ChangedAndConditions(); } }
    public bool Tuesday { get => tuesday; set { if (SetProperty(ref tuesday, value)) ChangedAndConditions(); } }
    public bool Wednesday { get => wednesday; set { if (SetProperty(ref wednesday, value)) ChangedAndConditions(); } }
    public bool Thursday { get => thursday; set { if (SetProperty(ref thursday, value)) ChangedAndConditions(); } }
    public bool Friday { get => friday; set { if (SetProperty(ref friday, value)) ChangedAndConditions(); } }
    public bool Saturday { get => saturday; set { if (SetProperty(ref saturday, value)) ChangedAndConditions(); } }
    public string IndividualDatesText { get => individualDatesText; set { if (SetProperty(ref individualDatesText, value)) ChangedAndConditions(); } }
    public bool UsesTimeRange { get => usesTimeRange; set { if (SetProperty(ref usesTimeRange, value)) ChangedAndConditions(); } }
    public TimeSpan StartTime { get => startTime; set { if (SetProperty(ref startTime, value)) Changed(); } }
    public TimeSpan EndTime { get => endTime; set { if (SetProperty(ref endTime, value)) Changed(); } }
    public bool AppliesToAllServices { get => appliesToAllServices; set { if (!SetProperty(ref appliesToAllServices, value)) return; ChangedAndConditions(); OnPropertyChanged(nameof(ShowServiceSelection)); } }
    public bool ShowServiceSelection => !AppliesToAllServices;
    public bool IsEnabled { get => isEnabled; set { if (SetProperty(ref isEnabled, value)) Changed(); } }
    public IReadOnlyList<SelectableServiceViewModel> Services { get => services; private set => SetProperty(ref services, value); }
    public bool HasNoDateOrTimeConditions => !Sunday && !Monday && !Tuesday && !Wednesday && !Thursday && !Friday && !Saturday &&
        !UsesNationalHolidays && string.IsNullOrWhiteSpace(IndividualDatesText) && !UsesTimeRange;
    public string ConditionExplanation => HasNoDateOrTimeConditions
        ? "曜日・祝日・個別日付・時間帯を指定していないため、対象サービスのすべての勤務に適用されます。"
        : "曜日・祝日・個別日付は、指定したもののいずれかに一致すれば対象です。時間帯を指定した場合は、さらにその時間帯と重なる勤務だけに適用されます。";

    public void Initialize(Guid? premiumId) { id = premiumId; InvalidateTrackedLoad(); OnPropertyChanged(nameof(PageTitle)); }

    public Task LoadAsync() => LoadTrackedAsync(LoadCoreAsync, force: true);
    public Task LoadIfNeededAsync() => LoadTrackedAsync(LoadCoreAsync, force: false);

    private Task LoadCoreAsync(CancellationToken token) => LoadEditorAsync(snapshot =>
    {
        source = id is { } premiumId ? snapshot.Premiums.FirstOrDefault(value => value.Id.Value == premiumId) : null;
        displayName = source?.DisplayName ?? string.Empty;
        selectedCalculationType = PremiumTypeOption.All.Single(value => value.Value == (source?.CalculationType ?? PremiumCalculationType.FixedPerHour));
        valueText = source?.CalculationType == PremiumCalculationType.Percentage
            ? (source.Percentage!.Value.Value / 100m).ToString("0.##", CultureInfo.InvariantCulture)
            : source?.Amount?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        usesNationalHolidays = source?.UsesNationalHolidays ?? false;
        sunday = source?.Weekdays.Contains(DayOfWeek.Sunday) ?? false;
        monday = source?.Weekdays.Contains(DayOfWeek.Monday) ?? false;
        tuesday = source?.Weekdays.Contains(DayOfWeek.Tuesday) ?? false;
        wednesday = source?.Weekdays.Contains(DayOfWeek.Wednesday) ?? false;
        thursday = source?.Weekdays.Contains(DayOfWeek.Thursday) ?? false;
        friday = source?.Weekdays.Contains(DayOfWeek.Friday) ?? false;
        saturday = source?.Weekdays.Contains(DayOfWeek.Saturday) ?? false;
        individualDatesText = source is null ? string.Empty : string.Join(", ", source.Dates.OrderBy(value => value).Select(value => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        usesTimeRange = source?.StartTime is not null;
        startTime = source?.StartTime is { } start ? TimeSpan.FromMinutes(start.Value) : new TimeSpan(22, 0, 0);
        endTime = source?.EndTime is { } end ? TimeSpan.FromMinutes(end.Value) : new TimeSpan(5, 0, 0);
        appliesToAllServices = source is null || source.ServiceIds.Count == 0;
        isEnabled = source?.IsEnabled ?? true;
        Services = [.. snapshot.Services.OrderBy(value => value.DisplayOrder.Value).Select(value =>
            new SelectableServiceViewModel(value.Id, value.DisplayName, source?.ServiceIds.Contains(value.Id) ?? false))];
        foreach (var service in Services) service.PropertyChanged += (_, _) => { Changed(); OnPropertyChanged(nameof(ConditionExplanation)); };
        NotifyAll();
        return Task.CompletedTask;
    }, token);

    public Task SaveAsync() => RunBusyAsync(async token =>
    {
        var replacement = BuildReplacement();
        await ConfirmAndSaveAsync(replacement, "割増の変更を確認",
            "割増の変更は選択中の設定対象年月だけに適用します。他の年月の給与設定は変更しません。",
            "割増設定を保存しました。", null, token);
    });

    private SettingSnapshotReplacementDto BuildReplacement()
    {
        var name = DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ApplicationErrorException("PREMIUM_NAME_REQUIRED", "表示名を入力してください。", nameof(DisplayName));
        BasisPoints? percentage = null;
        YenAmount? amount = null;
        if (SelectedCalculationType.Value == PremiumCalculationType.Percentage)
        {
            if (!decimal.TryParse(ValueText?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var percent) || percent < 0)
                throw new ApplicationErrorException("PREMIUM_PERCENTAGE_INVALID", "割合を0%以上の数値で入力してください。", nameof(ValueText));
            percentage = new BasisPoints(decimal.ToInt32(decimal.Round(percent * 100m, 0, MidpointRounding.AwayFromZero)));
        }
        else
        {
            if (!long.TryParse(ValueText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var yen) || yen < 0)
                throw new ApplicationErrorException("PREMIUM_AMOUNT_INVALID", "加算額を0円以上の整数で入力してください。", nameof(ValueText));
            amount = new YenAmount(yen);
        }
        MinuteOfDay? start = null;
        MinuteOfDay? end = null;
        if (UsesTimeRange)
        {
            start = new MinuteOfDay((int)StartTime.TotalMinutes);
            end = new MinuteOfDay((int)EndTime.TotalMinutes);
            if (start == end) throw new ApplicationErrorException("PREMIUM_TIME_INVALID", "開始時刻と終了時刻は異なる時刻にしてください。", nameof(EndTime));
        }
        var dates = ParseDates();
        var weekdays = new HashSet<DayOfWeek>();
        if (Sunday) weekdays.Add(DayOfWeek.Sunday); if (Monday) weekdays.Add(DayOfWeek.Monday);
        if (Tuesday) weekdays.Add(DayOfWeek.Tuesday); if (Wednesday) weekdays.Add(DayOfWeek.Wednesday);
        if (Thursday) weekdays.Add(DayOfWeek.Thursday); if (Friday) weekdays.Add(DayOfWeek.Friday); if (Saturday) weekdays.Add(DayOfWeek.Saturday);
        var serviceIds = AppliesToAllServices ? new HashSet<ServiceId>() : Services.Where(value => value.IsSelected).Select(value => value.Id).ToHashSet();
        if (!AppliesToAllServices && serviceIds.Count == 0)
            throw new ApplicationErrorException("PREMIUM_SERVICE_REQUIRED", "対象サービスを1つ以上選ぶか、全サービス対象を選択してください。", nameof(AppliesToAllServices));
        var premium = new SnapshotPremium(source?.Id ?? new PremiumId(Guid.NewGuid()), name, SelectedCalculationType.Value,
            percentage, amount, start, end, UsesNationalHolidays, weekdays, dates, serviceIds, IsEnabled);
        var premiums = Snapshot.Premiums.Where(value => value.Id != premium.Id).Append(premium).ToArray();
        return new(Snapshot.Services, Snapshot.TimeCategories, Snapshot.Rates, premiums, Snapshot.CountBonuses);
    }

    private HashSet<DateOnly> ParseDates()
    {
        var result = new HashSet<DateOnly>();
        foreach (var text in (IndividualDatesText ?? string.Empty).Split([',', '、', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new ApplicationErrorException("PREMIUM_DATE_INVALID", "個別日付は yyyy-MM-dd 形式で入力してください。", nameof(IndividualDatesText));
            result.Add(date);
        }
        return result;
    }

    private void ChangedAndConditions() { Changed(); OnPropertyChanged(nameof(HasNoDateOrTimeConditions)); OnPropertyChanged(nameof(ConditionExplanation)); }

    private void NotifyAll()
    {
        foreach (var name in new[] { nameof(DisplayName), nameof(SelectedCalculationType), nameof(ValueLabel), nameof(ValueText),
                     nameof(UsesNationalHolidays), nameof(Sunday), nameof(Monday), nameof(Tuesday), nameof(Wednesday), nameof(Thursday),
                     nameof(Friday), nameof(Saturday), nameof(IndividualDatesText), nameof(UsesTimeRange), nameof(StartTime), nameof(EndTime),
                     nameof(AppliesToAllServices), nameof(ShowServiceSelection), nameof(IsEnabled), nameof(HasNoDateOrTimeConditions), nameof(ConditionExplanation) })
            OnPropertyChanged(name);
    }
}
