using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Features.Startup;

namespace TkpSalaryCalculator.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly StartupPage startupPage;
    private readonly StartupViewModel startupViewModel;
    private readonly AppStartupCoordinator startupCoordinator;
    private readonly AppRootNavigator rootNavigator;

    public App(
        StartupPage startupPage,
        StartupViewModel startupViewModel,
        AppStartupCoordinator startupCoordinator,
        AppRootNavigator rootNavigator)
    {
        InitializeComponent();
        this.startupPage = startupPage ?? throw new ArgumentNullException(nameof(startupPage));
        this.startupViewModel = startupViewModel ?? throw new ArgumentNullException(nameof(startupViewModel));
        this.startupCoordinator = startupCoordinator ?? throw new ArgumentNullException(nameof(startupCoordinator));
        this.rootNavigator = rootNavigator ?? throw new ArgumentNullException(nameof(rootNavigator));
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
