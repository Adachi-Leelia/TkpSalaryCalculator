using System.Globalization;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Home;

/// <summary>SCR-CALC-01 の給与計算内訳を表示します。</summary>
public partial class CalculationDetailPage : ContentPage, IQueryAttributable
{
    public const string PayrollPeriodParameter = "payrollPeriod";
    public const string DateParameter = "date";
    public const string WorkRecordIdParameter = "workRecordId";
    private readonly CalculationDetailViewModel viewModel;

    public CalculationDetailPage(CalculationDetailViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (TryRead(query, DateParameter, out var dateText) &&
            TryRead(query, WorkRecordIdParameter, out var idText) &&
            DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
            Guid.TryParse(idText, out var id))
        {
            viewModel.SetWorkRecord(date, new WorkRecordId(id));
            return;
        }

        if (TryRead(query, PayrollPeriodParameter, out var periodText) &&
            DateOnly.TryParseExact($"{periodText}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            viewModel.SetPayrollPeriod(new PayrollPeriodKey(new YearMonth(month.Year, month.Month)));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadIfNeededAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.CancelPendingOperations();
        base.OnDisappearing();
    }

    private static bool TryRead(IDictionary<string, object> query, string key, out string value)
    {
        value = query.TryGetValue(key, out var item)
            ? Uri.UnescapeDataString(item?.ToString() ?? string.Empty)
            : string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
