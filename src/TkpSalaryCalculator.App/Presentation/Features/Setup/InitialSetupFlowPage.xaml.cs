namespace TkpSalaryCalculator.App.Presentation.Features.Setup;

public partial class InitialSetupFlowPage : ContentPage
{
    private readonly InitialSetupFlowViewModel viewModel;

    public InitialSetupFlowPage(InitialSetupFlowViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.CancelPendingOperations();
        base.OnDisappearing();
    }
}
