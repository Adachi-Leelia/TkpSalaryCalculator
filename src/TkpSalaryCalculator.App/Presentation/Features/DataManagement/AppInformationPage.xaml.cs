namespace TkpSalaryCalculator.App.Presentation.Features.DataManagement;

public partial class AppInformationPage : ContentPage
{
    private readonly AppInformationViewModel viewModel;

    public AppInformationPage(AppInformationViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
    }

    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
}
