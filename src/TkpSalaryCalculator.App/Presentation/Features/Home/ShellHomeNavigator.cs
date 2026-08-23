using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Features.Settings;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Home;

/// <summary>ホームの操作を Shell のタブまたは給与期間別ルートへ接続します。</summary>
public sealed class ShellHomeNavigator(IAppSessionState sessionState) : IHomeNavigator
{
    private readonly IAppSessionState sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));

    public Task OpenCalendarAsync(DateOnly selectedDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sessionState.SelectedCalendarDate = selectedDate;
        sessionState.CalendarMonth = new YearMonth(selectedDate.Year, selectedDate.Month);
        sessionState.SelectedRootRoute = NavigationRoutes.Calendar;
        return NavigateAsync($"//main/{NavigationRoutes.Calendar}", null, cancellationToken);
    }

    public Task OpenCalculationDetailsAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken) =>
        NavigatePeriodAsync(NavigationRoutes.CalculationDetails, "計算内訳", payrollPeriodKey, cancellationToken);

    public Task OpenMonthlyAllowancesAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken) =>
        NavigatePeriodAsync(NavigationRoutes.MonthlyAllowances, "月額手当", payrollPeriodKey, cancellationToken);

    public Task OpenUncalculatedDaysAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken) =>
        NavigatePeriodAsync(NavigationRoutes.UncalculatedDays, "未計算の勤務", payrollPeriodKey, cancellationToken);

    private static Task NavigatePeriodAsync(
        string route,
        string destination,
        PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken)
    {
        var parameters = new ShellNavigationQueryParameters
        {
            [HomeDestinationPage.DestinationParameter] = destination,
            [HomeDestinationPage.PayrollPeriodParameter] =
                $"{payrollPeriodKey.Value.Year:D4}-{payrollPeriodKey.Value.Month:D2}",
        };
        return NavigateAsync(route, parameters, cancellationToken);
    }

    private static Task NavigateAsync(
        string route,
        ShellNavigationQueryParameters? parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shell = Shell.Current ?? throw new InvalidOperationException("画面遷移を開始できません。");
            if (parameters is null)
                await shell.GoToAsync(route);
            else
                await shell.GoToAsync(route, parameters);
        });
    }
}

internal static class HomeRouteRegistration
{
    private static int registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0) return;
        Routing.RegisterRoute(NavigationRoutes.CalculationDetails, typeof(CalculationDetailPage));
        Routing.RegisterRoute(NavigationRoutes.MonthlyAllowances, typeof(MonthlyAllowancePage));
        Routing.RegisterRoute(NavigationRoutes.UncalculatedDays, typeof(HomeDestinationPage));
    }
}
