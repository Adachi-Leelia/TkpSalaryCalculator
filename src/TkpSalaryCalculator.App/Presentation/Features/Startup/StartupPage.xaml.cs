namespace TkpSalaryCalculator.App.Presentation.Features.Startup;

public partial class StartupPage : ContentPage
{
    public StartupPage(StartupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
