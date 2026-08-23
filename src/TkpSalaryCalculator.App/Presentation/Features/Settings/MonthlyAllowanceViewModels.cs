using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

public sealed record MonthlyAllowanceRow(
    MonthlyAllowanceId Id,
    string DisplayName,
    string AmountText,
    Func<Task> Edit,
    Func<Task> Delete,
    Action<Exception> OnException)
{
    public AsyncCommand EditCommand { get; } = new(Edit, OnException);
    public AsyncCommand DeleteCommand { get; } = new(Delete, OnException);
}

/// <summary>SCR-ALLOWANCE-01 の給与期間選択と手当一覧を管理します。</summary>
public sealed class MonthlyAllowanceViewModel : ViewModelBase
{
    private readonly IPayrollPeriodSettingsUseCase periods;
    private readonly ISalaryQueryUseCase salaryQuery;
    private readonly ISettingsNavigator navigator;
    private readonly IConfirmationDialogService dialogs;
    private readonly IAppSessionState sessionState;
    private readonly IUtcClock clock;
    private readonly ILocalDateConverter localDates;
    private readonly JapaneseDisplayFormatter formatter;
    private PayrollPeriodKey? periodKey;
    private string periodText = string.Empty;
    private string totalText = "0円";
    private string successMessage = string.Empty;
    private IReadOnlyList<MonthlyAllowanceRow> rows = [];

    public MonthlyAllowanceViewModel(
        IPayrollPeriodSettingsUseCase periods,
        ISalaryQueryUseCase salaryQuery,
        ISettingsNavigator navigator,
        IConfirmationDialogService dialogs,
        IAppSessionState sessionState,
        IUtcClock clock,
        ILocalDateConverter localDates,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        this.periods = periods ?? throw new ArgumentNullException(nameof(periods));
        this.salaryQuery = salaryQuery ?? throw new ArgumentNullException(nameof(salaryQuery));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        this.sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.localDates = localDates ?? throw new ArgumentNullException(nameof(localDates));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        PreviousCommand = new AsyncCommand(() => MoveAsync(-1), PresentError);
        NextCommand = new AsyncCommand(() => MoveAsync(1), PresentError);
        AddCommand = new AsyncCommand(AddAsync, PresentError);
        ReloadCommand = new AsyncCommand(LoadAsync, PresentError);
    }

    public string PeriodText { get => periodText; private set => SetProperty(ref periodText, value); }
    public string TotalText { get => totalText; private set => SetProperty(ref totalText, value); }
    public IReadOnlyList<MonthlyAllowanceRow> Rows
    {
        get => rows;
        private set
        {
            if (!SetProperty(ref rows, value)) return;
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(HasNoRows));
        }
    }
    public bool HasRows => Rows.Count != 0;
    public bool HasNoRows => !HasRows;
    public string SuccessMessage
    {
        get => successMessage;
        private set
        {
            if (!SetProperty(ref successMessage, value)) return;
            OnPropertyChanged(nameof(HasSuccessMessage));
        }
    }
    public bool HasSuccessMessage => !string.IsNullOrWhiteSpace(SuccessMessage);
    public AsyncCommand PreviousCommand { get; }
    public AsyncCommand NextCommand { get; }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand ReloadCommand { get; }

    public void SetPeriod(PayrollPeriodKey? value) => periodKey = value;
    public void SetSuccessMessage(string? value) => SuccessMessage = value ?? string.Empty;

    public Task LoadAsync() => RunBusyAsync(LoadCoreAsync);

    public Task MoveAsync(int offset) => RunBusyAsync(async cancellationToken =>
    {
        if (periodKey is null) await ResolvePeriodAsync(cancellationToken);
        periodKey = new PayrollPeriodKey(periodKey!.Value.Value.AddMonths(offset));
        sessionState.PayrollPeriod = periodKey;
        SuccessMessage = string.Empty;
        await LoadCoreAsync(cancellationToken);
    });

    public Task AddAsync() => periodKey is null
        ? Task.CompletedTask
        : navigator.OpenMonthlyAllowanceEditorAsync(periodKey.Value, null, CancellationToken.None);

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        await ResolvePeriodAsync(cancellationToken);
        var key = periodKey!.Value;
        var summaryTask = salaryQuery.GetPayrollPeriodAsync(key, cancellationToken);
        var allowanceTask = periods.GetAllowancesAsync(key, cancellationToken);
        await Task.WhenAll(summaryTask, allowanceTask);
        var summary = await summaryTask;
        var allowances = await allowanceTask;
        PeriodText = $"対象給与期間: {key.Value.Year}年{key.Value.Month}月分（{formatter.Date(summary.Period.StartDate, false)}～{formatter.Date(summary.Period.EndDate, false)}）";
        TotalText = $"手当合計: {formatter.Money(new YenAmount(allowances.Sum(x => x.Amount.Value)))}";
        Rows = allowances.Select(x => new MonthlyAllowanceRow(
            x.Id, x.DisplayName, formatter.Money(x.Amount),
            () => navigator.OpenMonthlyAllowanceEditorAsync(key, x.Id.Value, CancellationToken.None),
            () => DeleteAsync(x), PresentError)).ToArray();
        sessionState.PayrollPeriod = key;
    }

    private async Task ResolvePeriodAsync(CancellationToken cancellationToken)
    {
        if (periodKey is not null) return;
        periodKey = sessionState.PayrollPeriod ??
            (await periods.FindPeriodAsync(localDates.ToLocalDate(clock.UtcNow), cancellationToken)).Key;
    }

    private Task DeleteAsync(MonthlyAllowanceDto value) => RunBusyAsync(async cancellationToken =>
    {
        var confirmed = await dialogs.ConfirmAsync(
            "月額手当を削除しますか", $"「{value.DisplayName}」{formatter.Money(value.Amount)}を削除します。対象給与期間の合計から外れます。",
            "削除", "キャンセル", cancellationToken);
        if (!confirmed) return;
        await periods.DeleteAllowanceAsync(value.Id, cancellationToken);
        SuccessMessage = "月額手当を削除しました。";
        await LoadCoreAsync(cancellationToken);
    });
}

/// <summary>SCR-ALLOWANCE-02 の追加・編集と未保存破棄を管理します。</summary>
public sealed class MonthlyAllowanceEditorViewModel : EditableViewModelBase
{
    private readonly IPayrollPeriodSettingsUseCase periods;
    private readonly ISettingsNavigator navigator;
    private PayrollPeriodKey periodKey;
    private MonthlyAllowanceId? id;
    private string displayName = string.Empty;
    private string amountText = "0";
    private string periodText = string.Empty;

    public MonthlyAllowanceEditorViewModel(
        IPayrollPeriodSettingsUseCase periods,
        ISettingsNavigator navigator,
        IUserErrorPresenter errorPresenter,
        IConfirmationDialogService dialogs) : base(errorPresenter, dialogs)
    {
        this.periods = periods ?? throw new ArgumentNullException(nameof(periods));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        SaveCommand = new AsyncCommand(SaveAsync, PresentError);
    }

    public string PageTitle => id is null ? "月額手当を追加" : "月額手当を編集";
    public string PeriodText { get => periodText; private set => SetProperty(ref periodText, value); }
    public string DisplayName { get => displayName; set { if (SetProperty(ref displayName, value)) MarkDirty(); } }
    public string AmountText { get => amountText; set { if (SetProperty(ref amountText, value)) MarkDirty(); } }
    public AsyncCommand SaveCommand { get; }

    public void Initialize(PayrollPeriodKey key, MonthlyAllowanceId? allowanceId)
    {
        periodKey = key;
        id = allowanceId;
        PeriodText = $"対象給与期間: {key.Value.Year}年{key.Value.Month}月分";
        OnPropertyChanged(nameof(PageTitle));
    }

    public Task LoadAsync() => RunBusyAsync(async cancellationToken =>
    {
        if (id is { } allowanceId)
        {
            var existing = (await periods.GetAllowancesAsync(periodKey, cancellationToken)).FirstOrDefault(x => x.Id == allowanceId)
                ?? throw new ApplicationErrorException("ALLOWANCE_NOT_FOUND", "編集する月額手当が見つかりませんでした。");
            DisplayName = existing.DisplayName;
            AmountText = existing.Amount.Value.ToString();
        }
        MarkSaved();
    });

    public Task SaveAsync() => RunBusyAsync(async cancellationToken =>
    {
        if (!long.TryParse(AmountText, out var amount) || amount < 0)
            throw new ApplicationErrorException("ALLOWANCE_AMOUNT_INVALID", "金額は0円以上の整数で入力してください。", "Amount");
        await periods.SaveAllowanceAsync(new SaveMonthlyAllowanceCommand(id, periodKey, DisplayName, new YenAmount(amount)), cancellationToken);
        MarkSaved();
        await navigator.GoBackAsync("月額手当を保存しました。", cancellationToken);
    });
}
