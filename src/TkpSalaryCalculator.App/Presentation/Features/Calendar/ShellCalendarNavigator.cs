using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Features.Home;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Calendar;

/// <summary>Calendar 機能の遷移を Shell ルートへ接続します。</summary>
public sealed class ShellCalendarNavigator : ICalendarNavigator
{
    public Task OpenDayAsync(DateOnly date, CancellationToken cancellationToken) => NavigateAsync(
        NavigationRoutes.Day,
        new ShellNavigationQueryParameters { [DayPage.DateParameter] = date.ToString("yyyy-MM-dd") },
        cancellationToken);

    public Task OpenWorkEditorAsync(DateOnly date, WorkRecordId? workRecordId, CancellationToken cancellationToken)
    {
        var parameters = new ShellNavigationQueryParameters
        {
            [WorkEditorPage.DateParameter] = date.ToString("yyyy-MM-dd"),
        };
        if (workRecordId is { } id) parameters[WorkEditorPage.WorkRecordIdParameter] = id.Value.ToString("D");
        return NavigateAsync(NavigationRoutes.WorkEditor, parameters, cancellationToken);
    }

    public Task OpenCalculationDetailsAsync(DateOnly date, WorkRecordId workRecordId, CancellationToken cancellationToken) =>
        NavigateAsync(
            NavigationRoutes.CalculationDetails,
            new ShellNavigationQueryParameters
            {
                [CalculationDetailPage.DateParameter] = date.ToString("yyyy-MM-dd"),
                [CalculationDetailPage.WorkRecordIdParameter] = workRecordId.Value.ToString("D"),
            },
            cancellationToken);

    public async Task GoBackAsync(string? successMessage, CancellationToken cancellationToken)
    {
        await NavigateAsync("..", null, cancellationToken);
        if (string.IsNullOrWhiteSpace(successMessage)) return;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = Shell.Current?.CurrentPage
                ?? throw new InvalidOperationException("画面遷移先を取得できません。");
            await page.DisplayAlertAsync("完了", successMessage, "OK");
        });
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

internal static class CalendarRouteRegistration
{
    private static int registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0) return;
        Routing.RegisterRoute(NavigationRoutes.Day, typeof(DayPage));
        Routing.RegisterRoute(NavigationRoutes.WorkEditor, typeof(WorkEditorPage));
    }
}
