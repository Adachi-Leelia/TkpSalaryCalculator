using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

public sealed record ClosingDayOption(int? Value, string DisplayName)
{
    public static IReadOnlyList<ClosingDayOption> All { get; } =
        [new(null, "月末"), .. Enumerable.Range(1, 31).Select(value => new ClosingDayOption(value, $"{value}日"))];
}

/// <summary>SCR-PERIOD-01 の締め日履歴を、変更前後の期間確認付きで編集します。</summary>
public sealed class PayrollPeriodSettingsViewModel : EditableViewModelBase
{
    private readonly SettingsMonthContext context;
    private readonly IPayrollPeriodSettingsUseCase settings;
    private readonly IConfirmationDialogService dialogs;
    private readonly JapaneseDisplayFormatter formatter;
    private readonly ISettingsNavigator navigator;
    private ClosingDayOption selectedClosingDay = ClosingDayOption.All[0];
    private string currentRuleText = string.Empty;

    public PayrollPeriodSettingsViewModel(
        SettingsMonthContext context,
        IPayrollPeriodSettingsUseCase settings,
        IConfirmationDialogService dialogs,
        JapaneseDisplayFormatter formatter,
        ISettingsNavigator navigator,
        IUserErrorPresenter errorPresenter) : base(errorPresenter, dialogs)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        PreviousMonthCommand = new AsyncCommand(() => MoveMonthAsync(-1), PresentError);
        NextMonthCommand = new AsyncCommand(() => MoveMonthAsync(1), PresentError);
        SaveCommand = new AsyncCommand(SaveAsync, PresentError);
    }

    public IReadOnlyList<ClosingDayOption> ClosingDays => ClosingDayOption.All;
    public string EffectiveMonthText => $"適用開始給与期間年月: {formatter.Month(context.SelectedMonth)}";
    public string ScopeExplanation => $"この締め日は{formatter.Month(context.SelectedMonth)}以降の給与期間へ適用し、それより前の給与期間は変更しません。";
    public string CurrentRuleText { get => currentRuleText; private set => SetProperty(ref currentRuleText, value); }
    public ClosingDayOption SelectedClosingDay
    {
        get => selectedClosingDay;
        set { if (SetProperty(ref selectedClosingDay, value ?? ClosingDayOption.All[0])) MarkDirty(); }
    }
    public AsyncCommand PreviousMonthCommand { get; }
    public AsyncCommand NextMonthCommand { get; }
    public AsyncCommand SaveCommand { get; }

    public Task LoadAsync() => RunBusyAsync(LoadCoreAsync);

    public Task MoveMonthAsync(int offset) => RunBusyAsync(async token =>
    {
        if (!await CanLeaveAsync(token)) return;
        await context.MoveAsync(offset, token);
        await LoadCoreAsync(token);
    });

    public Task SaveAsync() => RunBusyAsync(async token =>
    {
        var command = new ReplaceClosingRuleCommand(new PayrollPeriodKey(context.SelectedMonth), SelectedClosingDay.Value);
        var preview = await settings.PreviewClosingRuleReplacementAsync(command, token);
        var before = preview.CurrentPeriod is null ? "現在の履歴では期間を算出できません。" : formatter.PayrollPeriod(preview.CurrentPeriod);
        var message = string.Join(Environment.NewLine,
            ScopeExplanation,
            string.Empty,
            "変更前の最初の給与期間",
            before,
            string.Empty,
            "変更後の最初の給与期間",
            formatter.PayrollPeriod(preview.ReplacementPeriod));
        if (!await dialogs.ConfirmAsync("締め日変更の影響を確認", message, "保存", "キャンセル", token)) return;
        await settings.ReplaceClosingRuleAsync(command, preview.ConfirmationToken, token);
        MarkSaved();
        await navigator.GoBackAsync("給与期間設定を保存しました。", token);
    });

    private async Task LoadCoreAsync(CancellationToken token)
    {
        var key = new PayrollPeriodKey(context.SelectedMonth);
        var rule = await settings.GetClosingRuleAsync(key, token);
        selectedClosingDay = ClosingDayOption.All.Single(value => value.Value == rule?.ClosingDay);
        CurrentRuleText = rule is null
            ? "現在有効な締め日はありません。"
            : $"現在有効: {(rule.ClosingDay is null ? "月末締め" : $"{rule.ClosingDay}日締め")}（{formatter.Month(rule.EffectiveFrom.Value)}から）";
        OnPropertyChanged(nameof(SelectedClosingDay));
        OnPropertyChanged(nameof(EffectiveMonthText));
        OnPropertyChanged(nameof(ScopeExplanation));
        MarkSaved();
    }
}
