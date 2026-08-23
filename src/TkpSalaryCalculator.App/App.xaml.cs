using Microsoft.Extensions.DependencyInjection;
using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Features.Startup;

namespace TkpSalaryCalculator.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly StartupPage startupPage;
    private readonly StartupViewModel startupViewModel;
    private readonly AppStartupCoordinator startupCoordinator;
    private readonly AppRootNavigator rootNavigator;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();

        startupPage = serviceProvider.GetRequiredService<StartupPage>();
        startupViewModel = serviceProvider.GetRequiredService<StartupViewModel>();
        startupCoordinator = serviceProvider.GetRequiredService<AppStartupCoordinator>();
        rootNavigator = serviceProvider.GetRequiredService<AppRootNavigator>();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(startupPage);

        rootNavigator.Attach(window);
        startupViewModel.SetStartupOperation(startupCoordinator.StartAsync);
        _ = startupViewModel.StartAsync();

        return window;
    }
}