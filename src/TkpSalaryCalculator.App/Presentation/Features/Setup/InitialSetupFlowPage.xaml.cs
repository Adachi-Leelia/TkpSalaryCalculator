namespace TkpSalaryCalculator.App.Presentation.Features.Setup;

public partial class InitialSetupFlowPage : ContentPage
{
    public InitialSetupFlowPage(InitialSetupFlowViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
