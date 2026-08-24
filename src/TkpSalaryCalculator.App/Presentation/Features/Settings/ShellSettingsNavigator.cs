using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Features.DataManagement;
using TkpSalaryCalculator.App.Presentation.Features.Home;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

/// <summary>設定機能の画面遷移を Shell へ接続します。</summary>
public sealed class ShellSettingsNavigator : ISettingsNavigator
{
    public Task OpenServicesAsync(CancellationToken cancellationToken) =>
        NavigateAsync(NavigationRoutes.ServiceSettings, null, cancellationToken);

    public Task OpenServiceEditorAsync(ServiceSettingsEditorMode mode, Guid? id, CancellationToken cancellationToken)
    {
        var parameters = new ShellNavigationQueryParameters
        {
            [ServiceSettingsEditorPage.ModeParameter] = mode.ToString(),
        };
        if (id is { } value) parameters[ServiceSettingsEditorPage.IdParameter] = value.ToString("D");
        return NavigateAsync(NavigationRoutes.ServiceSettingsEditor, parameters, cancellationToken);
    }

    public Task OpenPremiumsAsync(CancellationToken cancellationToken) =>
        NavigateAsync(NavigationRoutes.PremiumSettings, null, cancellationToken);

    public Task OpenPremiumEditorAsync(Guid? premiumId, CancellationToken cancellationToken) =>
        NavigateEditorAsync(NavigationRoutes.PremiumSettingsEditor, PremiumSettingsEditorPage.IdParameter, premiumId, cancellationToken);

    public Task OpenCountBonusesAsync(CancellationToken cancellationToken) =>
        NavigateAsync(NavigationRoutes.CountBonusSettings, null, cancellationToken);

    public Task OpenCountBonusEditorAsync(Guid? countBonusId, CancellationToken cancellationToken) =>
        NavigateEditorAsync(NavigationRoutes.CountBonusSettingsEditor, CountBonusSettingsEditorPage.IdParameter, countBonusId, cancellationToken);

    public Task OpenPayrollPeriodAsync(CancellationToken cancellationToken) =>
        NavigateAsync(NavigationRoutes.PayrollPeriodSettings, null, cancellationToken);

    public Task OpenMonthlyAllowancesAsync(CancellationToken cancellationToken) =>
        NavigateAsync(NavigationRoutes.MonthlyAllowances, null, cancellationToken);

    public Task OpenMonthlyAllowanceEditorAsync(PayrollPeriodKey payrollPeriodKey, Guid? allowanceId, CancellationToken cancellationToken)
    {
        var parameters = new ShellNavigationQueryParameters
        {
            [MonthlyAllowancePage.PayrollPeriodParameter] = $"{payrollPeriodKey.Value.Year:D4}-{payrollPeriodKey.Value.Month:D2}",
        };
        if (allowanceId is { } id) parameters[MonthlyAllowanceEditorPage.IdParameter] = id.ToString("D");
        return NavigateAsync(NavigationRoutes.MonthlyAllowanceEditor, parameters, cancellationToken);
    }

    public Task OpenBasicShiftsAsync(CancellationToken cancellationToken) =>
        NavigateAsync(NavigationRoutes.BasicShifts, null, cancellationToken);

    public Task OpenBasicShiftEditorAsync(Guid? basicShiftId, CancellationToken cancellationToken) =>
        NavigateEditorAsync(NavigationRoutes.BasicShiftEditor, BasicShiftEditorPage.IdParameter, basicShiftId, cancellationToken);

    public Task OpenDataManagementAsync(CancellationToken cancellationToken) =>
        NavigateAsync(NavigationRoutes.DataManagement, null, cancellationToken);

    public Task OpenAppInformationAsync(CancellationToken cancellationToken) =>
        NavigateAsync(NavigationRoutes.AppInformation, null, cancellationToken);

    public Task GoBackAsync(string? successMessage, CancellationToken cancellationToken)
    {
        var parameters = string.IsNullOrWhiteSpace(successMessage)
            ? null
            : new ShellNavigationQueryParameters { [SettingsPageQuery.SuccessMessageParameter] = successMessage };
        return NavigateAsync("..", parameters, cancellationToken);
    }

    private static Task NavigateEditorAsync(string route, string parameter, Guid? id, CancellationToken cancellationToken)
    {
        var parameters = id is null ? null : new ShellNavigationQueryParameters { [parameter] = id.Value.ToString("D") };
        return NavigateAsync(route, parameters, cancellationToken);
    }

    private static Task NavigateAsync(string route, ShellNavigationQueryParameters? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shell = Shell.Current ?? throw new InvalidOperationException("画面遷移を開始できません。");
            if (parameters is null) await shell.GoToAsync(route);
            else await shell.GoToAsync(route, parameters);
        });
    }
}

internal static class SettingsRouteRegistration
{
    private static int registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0) return;
        Routing.RegisterRoute(NavigationRoutes.ServiceSettings, typeof(ServiceSettingsPage));
        Routing.RegisterRoute(NavigationRoutes.ServiceSettingsEditor, typeof(ServiceSettingsEditorPage));
        Routing.RegisterRoute(NavigationRoutes.PremiumSettings, typeof(PremiumSettingsPage));
        Routing.RegisterRoute(NavigationRoutes.PremiumSettingsEditor, typeof(PremiumSettingsEditorPage));
        Routing.RegisterRoute(NavigationRoutes.CountBonusSettings, typeof(CountBonusSettingsPage));
        Routing.RegisterRoute(NavigationRoutes.CountBonusSettingsEditor, typeof(CountBonusSettingsEditorPage));
        Routing.RegisterRoute(NavigationRoutes.PayrollPeriodSettings, typeof(PayrollPeriodSettingsPage));
        Routing.RegisterRoute(NavigationRoutes.MonthlyAllowances, typeof(MonthlyAllowancePage));
        Routing.RegisterRoute(NavigationRoutes.MonthlyAllowanceEditor, typeof(MonthlyAllowanceEditorPage));
        Routing.RegisterRoute(NavigationRoutes.BasicShifts, typeof(BasicShiftPage));
        Routing.RegisterRoute(NavigationRoutes.BasicShiftEditor, typeof(BasicShiftEditorPage));
        Routing.RegisterRoute(NavigationRoutes.DataManagement, typeof(DataManagementPage));
        Routing.RegisterRoute(NavigationRoutes.AppInformation, typeof(AppInformationPage));
    }
}

internal static class SettingsPageQuery
{
    public const string SuccessMessageParameter = "successMessage";
}
