namespace TkpSalaryCalculator.App.Presentation.Features.Settings;
public partial class PayrollPeriodSettingsPage : ContentPage
{
    private readonly PayrollPeriodSettingsViewModel viewModel;
    public PayrollPeriodSettingsPage(PayrollPeriodSettingsViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
}
