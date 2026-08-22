using System.Globalization;

namespace TkpSalaryCalculator.App.Presentation.Features.Calendar;

/// <summary>SCR-DAY-01 の日別一覧を表示します。</summary>
public partial class DayPage : ContentPage, IQueryAttributable
{
    public const string DateParameter = "date";
    private readonly DayViewModel viewModel;

    public DayPage(DayViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(DateParameter, out var value) &&
            DateOnly.TryParseExact(value?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            viewModel.SetDate(date);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.CancelPendingOperations();
        base.OnDisappearing();
    }
}
