using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.UseCases;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

/// <summary>SCR-SET-01 の年月選択、前月コピー、および設定メニュー操作を管理します。</summary>
public sealed class SettingsMenuViewModel : ViewModelBase
{
    private readonly SettingsMonthContext context;
    private readonly IMonthSettingsUseCase settings;
    private readonly ISettingsNavigator navigator;
    private readonly IConfirmationDialogService dialogs;
    private readonly JapaneseDisplayFormatter formatter;
    private string? successMessage;

    public SettingsMenuViewModel(
        SettingsMonthContext context,
        IMonthSettingsUseCase settings,
        ISettingsNavigator navigator,
        IConfirmationDialogService dialogs,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        PreviousMonthCommand = new AsyncCommand(() => MoveMonthAsync(-1), PresentError);
        NextMonthCommand = new AsyncCommand(() => MoveMonthAsync(1), PresentError);
        CopyPreviousMonthCommand = new AsyncCommand(CopyPreviousMonthAsync, PresentError);
        OpenServicesCommand = new AsyncCommand(() => navigator.OpenServicesAsync(default), PresentError);
        OpenPremiumsCommand = new AsyncCommand(() => navigator.OpenPremiumsAsync(default), PresentError);
        OpenCountBonusesCommand = new AsyncCommand(() => navigator.OpenCountBonusesAsync(default), PresentError);
        OpenPayrollPeriodCommand = new AsyncCommand(() => navigator.OpenPayrollPeriodAsync(default), PresentError);
        OpenMonthlyAllowancesCommand = new AsyncCommand(() => navigator.OpenMonthlyAllowancesAsync(default), PresentError);
        OpenBasicShiftsCommand = new AsyncCommand(() => navigator.OpenBasicShiftsAsync(default), PresentError);
        OpenDataManagementCommand = new AsyncCommand(() => navigator.OpenDataManagementAsync(default), PresentError);
        OpenAppInformationCommand = new AsyncCommand(() => navigator.OpenAppInformationAsync(default), PresentError);
    }

    public string MonthHeaderText => context.HeaderText;
    public AsyncCommand PreviousMonthCommand { get; }
    public AsyncCommand NextMonthCommand { get; }
    public AsyncCommand CopyPreviousMonthCommand { get; }
    public AsyncCommand OpenServicesCommand { get; }
    public AsyncCommand OpenPremiumsCommand { get; }
    public AsyncCommand OpenCountBonusesCommand { get; }
    public AsyncCommand OpenPayrollPeriodCommand { get; }
    public AsyncCommand OpenMonthlyAllowancesCommand { get; }
    public AsyncCommand OpenBasicShiftsCommand { get; }
    public AsyncCommand OpenDataManagementCommand { get; }
    public AsyncCommand OpenAppInformationCommand { get; }

    public string? SuccessMessage
    {
        get => successMessage;
        private set
        {
            if (!SetProperty(ref successMessage, value)) return;
            OnPropertyChanged(nameof(HasSuccessMessage));
        }
    }

    public bool HasSuccessMessage => !string.IsNullOrWhiteSpace(SuccessMessage);

    public Task LoadAsync() => RunBusyAsync(async token =>
    {
        await context.RefreshAsync(token);
        OnPropertyChanged(nameof(MonthHeaderText));
    });

    public Task MoveMonthAsync(int offset) => RunBusyAsync(async token =>
    {
        await context.MoveAsync(offset, token);
        SuccessMessage = null;
        OnPropertyChanged(nameof(MonthHeaderText));
    });

    public Task CopyPreviousMonthAsync() => RunBusyAsync(async token =>
    {
        var preview = await settings.PreviewCopyPreviousMonthAsync(context.SelectedMonth, token);
        SettingsPreviewText.ThrowIfBlocking(preview.Issues);
        var confirmed = await dialogs.ConfirmAsync(
            "前月の設定をコピー",
            SettingsPreviewText.Replacement(preview, formatter,
                "前月の給与設定で選択中の1か月だけを置き換えます。他の年月は変更しません。"),
            "コピー", "キャンセル", token);
        if (!confirmed) return;
        var result = await settings.CopyPreviousMonthAsync(context.SelectedMonth, preview.ConfirmationToken, token);
        context.Accept(result);
        SuccessMessage = $"{formatter.Month(context.SelectedMonth)}へ前月の設定をコピーしました。";
    });

    public void SetSuccessMessage(string? message) => SuccessMessage = message;
}
