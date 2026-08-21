using Microsoft.Extensions.DependencyInjection;
using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Features.Calendar;
using TkpSalaryCalculator.App.Presentation.Features.Home;
using TkpSalaryCalculator.App.Presentation.Features.Settings;
using TkpSalaryCalculator.App.Presentation.Features.Setup;

namespace TkpSalaryCalculator.App;

public partial class AppShell : Shell
{
    private readonly AppRootKind rootKind;
    private readonly IAppSessionState sessionState;
    private readonly IUserErrorPresenter errorPresenter;

    public AppShell(IServiceProvider services, AppRootKind rootKind, IAppSessionState sessionState)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        errorPresenter = services.GetRequiredService<IUserErrorPresenter>();
        this.rootKind = rootKind;
        InitializeComponent();

        if (rootKind == AppRootKind.InitialSetup)
            ConfigureInitialSetup(services);
        else
            ConfigureMainTabs(services);

        Navigating += GuardNavigation;
        Navigated += RememberSelectedTab;
    }

    private void ConfigureInitialSetup(IServiceProvider services)
    {
        var content = CreateContent<InitialSetupFlowPage>(services, "初期設定", NavigationRoutes.InitialSetupContent);
        SetTabBarIsVisible(content, false);
        var tab = new Tab { Route = NavigationRoutes.InitialSetup, Title = "初期設定" };
        tab.Items.Add(content);
        var item = new FlyoutItem { Route = NavigationRoutes.InitialSetupRoot, FlyoutItemIsVisible = false };
        item.Items.Add(tab);
        Items.Add(item);
    }

    private void ConfigureMainTabs(IServiceProvider services)
    {
        var tabBar = new TabBar { Route = "main" };
        var home = CreateTab<HomePage>(services, "ホーム", NavigationRoutes.Home);
        var calendar = CreateTab<CalendarPage>(services, "カレンダー", NavigationRoutes.Calendar);
        var settings = CreateTab<SettingsMenuPage>(services, "設定", NavigationRoutes.Settings);
        tabBar.Items.Add(home);
        tabBar.Items.Add(calendar);
        tabBar.Items.Add(settings);
        tabBar.CurrentItem = sessionState.SelectedRootRoute switch
        {
            NavigationRoutes.Calendar => calendar,
            NavigationRoutes.Settings => settings,
            _ => home,
        };
        Items.Add(tabBar);
    }

    private static Tab CreateTab<TPage>(IServiceProvider services, string title, string route)
        where TPage : Page
    {
        var tab = new Tab { Title = title, Route = route };
        tab.Items.Add(CreateContent<TPage>(services, title, $"{route}-content"));
        return tab;
    }

    private static ShellContent CreateContent<TPage>(IServiceProvider services, string title, string route)
        where TPage : Page => new()
        {
            Title = title,
            Route = route,
            ContentTemplate = new DataTemplate(() => services.GetRequiredService<TPage>()),
        };

    private async void GuardNavigation(object? sender, ShellNavigatingEventArgs eventArgs)
    {
        var target = eventArgs.Target.Location.OriginalString;
        if (rootKind == AppRootKind.InitialSetup)
        {
            if (!NavigationRoutes.IsInitialSetupLocation(target)) eventArgs.Cancel();
            return;
        }

        if (CurrentPage?.BindingContext is not ILeaveGuard leaveGuard) return;

        var deferral = eventArgs.GetDeferral();
        try
        {
            if (!await leaveGuard.CanLeaveAsync()) eventArgs.Cancel();
        }
        catch (OperationCanceledException)
        {
            eventArgs.Cancel();
        }
        catch (Exception exception)
        {
            eventArgs.Cancel();
            await ShowNavigationErrorAsync(exception);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void RememberSelectedTab(object? sender, ShellNavigatedEventArgs eventArgs)
    {
        if (rootKind != AppRootKind.Main) return;
        var location = eventArgs.Current.Location.OriginalString;
        var route = NavigationRoutes.GetMainTab(location);
        if (route is not null) sessionState.SelectedRootRoute = route;
    }

    private async Task ShowNavigationErrorAsync(Exception exception)
    {
        try
        {
            var page = CurrentPage;
            if (page is not null)
            {
                await page.DisplayAlertAsync(
                    "画面を移動できません",
                    errorPresenter.GetMessage(exception),
                    "OK");
            }
        }
        catch (Exception displayException)
        {
            System.Diagnostics.Debug.WriteLine(displayException);
        }
    }
}
