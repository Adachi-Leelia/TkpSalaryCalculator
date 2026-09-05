using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.UseCases;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

public sealed record AnnualClosingMonthOption(int Value, string DisplayName)
{
    public static IReadOnlyList<AnnualClosingMonthOption> All { get; } =
        [.. Enumerable.Range(1, 12).Select(value => new AnnualClosingMonthOption(value, $"{value}月"))];
}

/// <summary>SCR-ANNUAL-01 の年間締め月を編集します。</summary>
public sealed class AnnualSummarySettingsViewModel : EditableViewModelBase
{
    private readonly IAnnualSummarySettingsUseCase settings;
    private readonly ISettingsNavigator navigator;
    private readonly IAppSessionState sessionState;
    private AnnualClosingMonthOption selectedClosingMonth = AnnualClosingMonthOption.All[^1];

    public AnnualSummarySettingsViewModel(
        IAnnualSummarySettingsUseCase settings,
        ISettingsNavigator navigator,
        IAppSessionState sessionState,
        IConfirmationDialogService dialogs,
        IUserErrorPresenter errorPresenter) : base(errorPresenter, dialogs)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        TrackDataChanges(sessionState, AppDataChangeKind.AnnualSummarySettings);
        SaveCommand = new AsyncCommand(SaveAsync, PresentError);
    }

    public IReadOnlyList<AnnualClosingMonthOption> ClosingMonths => AnnualClosingMonthOption.All;

    public AnnualClosingMonthOption SelectedClosingMonth
    {
        get => selectedClosingMonth;
        set
        {
            if (!SetProperty(ref selectedClosingMonth, value ?? AnnualClosingMonthOption.All[^1])) return;
            OnPropertyChanged(nameof(AnnualPeriodExample));
            MarkDirty();
        }
    }

    public string AnnualPeriodExample => SelectedClosingMonth.Value == 12
        ? "年間区分の例: 1月分～12月分"
        : $"年間区分の例: 前年{SelectedClosingMonth.Value + 1}月分～当年{SelectedClosingMonth.Value}月分";

    public AsyncCommand SaveCommand { get; }

    public Task LoadAsync() => LoadTrackedAsync(LoadCoreAsync, force: true);

    public Task LoadIfNeededAsync() => LoadTrackedAsync(LoadCoreAsync, force: false);

    public Task SaveAsync() => RunBusyAsync(async token =>
    {
        var saved = await settings.SaveAsync(
            new SaveAnnualSummarySettingCommand(SelectedClosingMonth.Value), token);
        selectedClosingMonth = ClosingMonths.Single(option => option.Value == saved.ClosingMonth.Value);
        OnPropertyChanged(nameof(SelectedClosingMonth));
        OnPropertyChanged(nameof(AnnualPeriodExample));
        sessionState.NotifyDataChanged(
            AppDataChangeKind.AnnualSummarySettings | AppDataChangeKind.BackupStatus);
        MarkSaved();
        await navigator.GoBackAsync("年間累計設定を保存しました。", token);
    });

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var value = await settings.GetAsync(cancellationToken);
        selectedClosingMonth = ClosingMonths.Single(option => option.Value == value.ClosingMonth.Value);
        OnPropertyChanged(nameof(SelectedClosingMonth));
        OnPropertyChanged(nameof(AnnualPeriodExample));
        MarkSaved();
    }
}
