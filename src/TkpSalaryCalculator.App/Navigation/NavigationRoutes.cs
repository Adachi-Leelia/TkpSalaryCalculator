namespace TkpSalaryCalculator.App.Navigation;

/// <summary>ルート Shell で使用できる安定したルート名を定義します。</summary>
public static class NavigationRoutes
{
    public const string InitialSetupRoot = "setup-root";
    public const string InitialSetup = "initial-setup";
    public const string InitialSetupContent = "initial-setup-content";
    public const string Home = "home";
    public const string Calendar = "calendar";
    public const string Settings = "settings";

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
