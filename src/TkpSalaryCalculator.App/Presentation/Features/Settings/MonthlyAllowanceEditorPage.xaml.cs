using System.Globalization;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

public partial class MonthlyAllowanceEditorPage : ContentPage, IQueryAttributable
{
    public const string IdParameter = "allowanceId";
    private readonly MonthlyAllowanceEditorViewModel viewModel;
    public MonthlyAllowanceEditorPage(MonthlyAllowanceEditorViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue(MonthlyAllowancePage.PayrollPeriodParameter, out var value) ||
            !DateOnly.TryParseExact($"{value}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return;
        MonthlyAllowanceId? id = query.TryGetValue(IdParameter, out var raw) && Guid.TryParse(raw?.ToString(), out var parsed)
            ? new MonthlyAllowanceId(parsed)
            : null;
        viewModel.Initialize(new PayrollPeriodKey(new YearMonth(date.Year, date.Month)), id);
    }
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadIfNeededAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
}
