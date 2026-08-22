namespace TkpSalaryCalculator.App.Presentation.Features.Calendar;

/// <summary>SCR-CAL-01 のルートを確保する基盤ページです。</summary>
public partial class CalendarPage : ContentPage
{
    private readonly CalendarViewModel viewModel;

    public CalendarPage(CalendarViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
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
