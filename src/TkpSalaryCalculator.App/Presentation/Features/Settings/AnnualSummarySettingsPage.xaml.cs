namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

public partial class AnnualSummarySettingsPage : ContentPage
{
    private readonly AnnualSummarySettingsViewModel viewModel;

    public AnnualSummarySettingsPage(AnnualSummarySettingsViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
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
}
