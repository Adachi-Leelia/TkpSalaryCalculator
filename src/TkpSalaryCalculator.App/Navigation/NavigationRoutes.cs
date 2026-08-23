namespace TkpSalaryCalculator.App.Navigation;

/// <summary>ルート Shell で使用できる安定したルート名を定義します。</summary>
public static class NavigationRoutes
{
    public const string InitialSetupRoot = "setup-root";
    public const string InitialSetup = "initial-setup";
    public const string InitialSetupContent = "initial-setup-content";
    public const string Home = "home";
    public const string Calendar = "calendar";
    public const string Day = "day";
    public const string WorkEditor = "work-editor";
    public const string Settings = "settings";
    public const string ServiceSettings = "service-settings";
    public const string ServiceSettingsEditor = "service-settings-editor";
    public const string PremiumSettings = "premium-settings";
    public const string PremiumSettingsEditor = "premium-settings-editor";
    public const string CountBonusSettings = "count-bonus-settings";
    public const string CountBonusSettingsEditor = "count-bonus-settings-editor";
    public const string PayrollPeriodSettings = "payroll-period-settings";
    public const string MonthlyAllowanceEditor = "monthly-allowance-editor";
    public const string BasicShifts = "basic-shifts";
    public const string BasicShiftEditor = "basic-shift-editor";
    public const string DataManagement = "data-management";
    public const string AppInformation = "app-information";
    public const string CalculationDetails = "calculation-details";
    public const string MonthlyAllowances = "monthly-allowances";
    public const string UncalculatedDays = "uncalculated-days";

    public static bool IsMainTab(string? route) => route is Home or Calendar or Settings;

    public static bool IsInitialSetupLocation(string? location) =>
        GetRouteSegments(location) is { Length: > 0 } segments &&
        segments.All(static route => route is InitialSetupRoot or InitialSetup or InitialSetupContent);

    public static string? GetMainTab(string? location) =>
        GetRouteSegments(location).FirstOrDefault(IsMainTab);

    private static string[] GetRouteSegments(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return [];
        var pathEnd = location.IndexOfAny(['?', '#']);
        var path = pathEnd < 0 ? location : location[..pathEnd];
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
