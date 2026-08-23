using System.Globalization;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

public partial class MonthlyAllowancePage : ContentPage, IQueryAttributable
{
    public const string PayrollPeriodParameter = "payrollPeriod";
    private readonly MonthlyAllowanceViewModel viewModel;
    public MonthlyAllowancePage(MonthlyAllowanceViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        PayrollPeriodKey? key = null;
        if (query.TryGetValue(PayrollPeriodParameter, out var value) &&
            DateOnly.TryParseExact($"{value}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            key = new PayrollPeriodKey(new YearMonth(date.Year, date.Month));
        viewModel.SetPeriod(key);
        if (query.TryGetValue(SettingsPageQuery.SuccessMessageParameter, out var message)) viewModel.SetSuccessMessage(message?.ToString());
    }
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadIfNeededAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
}
