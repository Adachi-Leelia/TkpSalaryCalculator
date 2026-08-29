using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TkpSalaryCalculator.App.Tests;

public sealed class AppConfigurationTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void AppProject_IsAndroidOnlyApi29ApkAndUsesCompiledBindings()
    {
        var project = XDocument.Load(AppPath("TkpSalaryCalculator.App.csproj"));

        Assert.Equal("net10.0-android", Property(project, "TargetFramework"));
        Assert.Equal("true", Property(project, "UseMaui"));
        Assert.Equal("true", Property(project, "SingleProject"));
        Assert.Equal("true", Property(project, "MauiEnableXamlCBindingWithSourceCompilation"));
        Assert.Equal("29.0", Property(project, "SupportedOSPlatformVersion"));
        Assert.Equal("apk", Property(project, "AndroidPackageFormats"));
        Assert.Equal("ja-JP", Property(project, "NeutralLanguage"));

        var references = project.Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(path => path is not null)
            .ToArray();
        Assert.Contains(references, path => path!.EndsWith("TkpSalaryCalculator.Application.csproj", StringComparison.Ordinal));
        Assert.Contains(references, path => path!.EndsWith("TkpSalaryCalculator.Infrastructure.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void AndroidManifest_IsOfflineAndExcludedFromBackup()
    {
        var document = XDocument.Load(AppPath("Platforms", "Android", "AndroidManifest.xml"));
        XNamespace android = "http://schemas.android.com/apk/res/android";
        XNamespace tools = "http://schemas.android.com/tools";
        var application = Assert.Single(document.Descendants("application"));

        Assert.Equal("false", (string?)application.Attribute(android + "allowBackup"));
        Assert.Equal("false", (string?)application.Attribute(android + "usesCleartextTraffic"));
        Assert.NotNull(application.Attribute(android + "dataExtractionRules"));
        Assert.NotNull(application.Attribute(android + "fullBackupContent"));

        var permissions = document.Descendants("uses-permission").ToArray();
        foreach (var permissionName in new[]
                 {
                     "android.permission.INTERNET",
                     "android.permission.ACCESS_NETWORK_STATE",
                 })
        {
            var permission = Assert.Single(permissions, element =>
                (string?)element.Attribute(android + "name") == permissionName);
            Assert.Equal("remove", (string?)permission.Attribute(tools + "node"));
        }
    }

    [Fact]
    public void Shell_HasExactlyTheThreeSpecifiedMainTabsAndNoPortraitLock()
    {
        var shell = File.ReadAllText(AppPath("AppShell.xaml.cs"));
        var mainTabs = Regex.Matches(
            shell,
            @"CreateTab<(HomePage|CalendarPage|SettingsMenuPage)>",
            RegexOptions.CultureInvariant);
        var activity = File.ReadAllText(AppPath("Platforms", "Android", "MainActivity.cs"));

        Assert.Equal(3, mainTabs.Count);
        Assert.Contains("NavigationRoutes.Home", shell, StringComparison.Ordinal);
        Assert.Contains("NavigationRoutes.Calendar", shell, StringComparison.Ordinal);
        Assert.Contains("NavigationRoutes.Settings", shell, StringComparison.Ordinal);
        Assert.Contains("NavigationRoutes.IsInitialSetupLocation(target)", shell, StringComparison.Ordinal);
        Assert.Contains("CurrentPage?.BindingContext is not ILeaveGuard", shell, StringComparison.Ordinal);
        Assert.Contains("eventArgs.GetDeferral()", shell, StringComparison.Ordinal);
        Assert.Contains("leaveGuard.CanLeaveAsync()", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("ScreenOrientation", activity, StringComparison.Ordinal);
    }

    [Fact]
    public void CommonStateControls_HaveRequiredContentActionsAndAccessibilityMetadata()
    {
        Assert.True(File.Exists(AppPath("Resources", "Styles", "Colors.xaml")));
        Assert.True(File.Exists(AppPath("Resources", "Styles", "Styles.xaml")));

        var loading = LoadControl("LoadingStateView.xaml");
        AssertControlUsesCompiledBinding(loading);
        Assert.Single(loading.Descendants(), element => element.Name.LocalName == "ActivityIndicator");
        Assert.Contains(loading.Descendants(), element => HasAttribute(element, "SemanticProperties.Description"));

        var empty = LoadControl("EmptyStateView.xaml");
        AssertControlUsesCompiledBinding(empty);
        Assert.True(empty.Descendants().Count(element => element.Name.LocalName == "Label") >= 2);
        Assert.Contains(empty.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            HasAttribute(element, "Command") &&
            HasAttribute(element, "SemanticProperties.Description"));
        Assert.Contains(empty.Descendants(), element => HasAttribute(element, "SemanticProperties.Description"));

        var error = LoadControl("ErrorStateView.xaml");
        AssertControlUsesCompiledBinding(error);
        Assert.Contains(error.Descendants(), element =>
            element.Name.LocalName == "Label" &&
            AttributeValue(element, "Text").Contains("失敗", StringComparison.Ordinal));
        Assert.Contains(error.Descendants(), element =>
            element.Name.LocalName == "Button" && HasAttribute(element, "Command"));

        var uncalculated = LoadControl("UncalculatedStateView.xaml");
        AssertControlUsesCompiledBinding(uncalculated);
        Assert.Contains(uncalculated.Descendants(), element =>
            element.Name.LocalName == "Label" && AttributeValue(element, "Text") == "未計算");
        Assert.Contains(uncalculated.Descendants(), element => HasAttribute(element, "SemanticProperties.Description"));
        Assert.Contains(uncalculated.Descendants(), element => HasAttribute(element, "SemanticProperties.Hint"));

        var saveBar = LoadControl("FixedSaveBar.xaml");
        AssertControlUsesCompiledBinding(saveBar);
        Assert.Equal("All", AttributeValue(saveBar.Root!, "SafeAreaEdges"));
        Assert.Contains(saveBar.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            HasAttribute(element, "Command") &&
            HasAttribute(element, "IsEnabled") &&
            HasAttribute(element, "SemanticProperties.Description"));
    }

    [Fact]
    public void Presentation_DoesNotImplementSqlJsonOrSalaryCalculationInfrastructure()
    {
        var presentationRoot = AppPath("Presentation");
        var files = Directory.EnumerateFiles(presentationRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var forbidden = new[]
        {
            "Microsoft.Data.Sqlite",
            "TkpSalaryCalculator.Infrastructure",
            "System.Text.Json",
            "JsonSerializer",
            "new SalaryCalculator(",
            "ISalaryCalculator",
            "SELECT ",
            "INSERT ",
            "UPDATE ",
            "DELETE ",
        };

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (var value in forbidden)
            {
                Assert.DoesNotContain(value, source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void CompositionRoot_RegistersStartupUseCasesSessionAndPages()
    {
        var source = File.ReadAllText(AppPath("MauiProgram.cs"));
        var requiredRegistrations = new[]
        {
            "AddSingleton<IApplicationDatabaseInitializer, SqliteDatabaseInitializer>()",
            "AddSingleton<IInitialSetupUseCase, InitialSetupUseCase>()",
            "AddSingleton<IPayrollPeriodSettingsUseCase, PayrollPeriodSettingsUseCase>()",
            "AddSingleton<IAnnualSummarySettingRepository, SqliteAnnualSummarySettingRepository>()",
            "AddSingleton<IAnnualSummarySettingsUseCase, AnnualSummarySettingsUseCase>()",
            "AddSingleton<IAppSessionState>",
            "AddSingleton<AppStartupCoordinator>()",
            "AddSingleton<IConfirmationDialogService, ConfirmationDialogService>()",
            "AddSingleton<IHomeNavigator, ShellHomeNavigator>()",
            "AddSingleton<StartupViewModel>()",
            "AddSingleton<StartupPage>()",
            "AddTransient<InitialSetupFlowPage>()",
            "AddTransient<HomeViewModel>()",
            "AddTransient<HomePage>()",
            "AddTransient<CalendarPage>()",
            "AddTransient<SettingsMenuPage>()",
            "AddTransient<AnnualSummarySettingsPage>()",
        };

        foreach (var registration in requiredRegistrations)
        {
            Assert.Contains(registration, source, StringComparison.Ordinal);
        }

        var app = File.ReadAllText(AppPath("App.xaml.cs"));
        Assert.Contains("rootNavigator.Attach(window)", app, StringComparison.Ordinal);
        Assert.Contains("startupViewModel.SetStartupOperation(startupCoordinator.StartAsync)", app, StringComparison.Ordinal);
    }

    [Fact]
    public void AnnualSummarySettingsPageUsesPickerFixedSaveBarAndRequiredExplanation()
    {
        var page = XDocument.Load(AppPath(
            "Presentation", "Features", "Settings", "AnnualSummarySettingsPage.xaml"));
        AssertControlUsesCompiledBinding(page);
        Assert.Contains(page.Descendants(), element =>
            element.Name.LocalName == "Picker" &&
            AttributeValue(element, "ItemsSource") == "{Binding ClosingMonths}" &&
            AttributeValue(element, "SelectedItem") == "{Binding SelectedClosingMonth}");
        Assert.Contains(page.Descendants(), element =>
            element.Name.LocalName == "FixedSaveBar" &&
            AttributeValue(element, "SaveCommand") == "{Binding SaveCommand}");
        Assert.Contains(page.Descendants(), element =>
            element.Name.LocalName == "Label" &&
            AttributeValue(element, "Text").Contains(
                "年間累計の区切りだけを変更し、給与額や勤務記録は変更しません",
                StringComparison.Ordinal));
    }

    [Fact]
    public void HomePage_ShowsCompletePeriodSummaryNavigationAndAccessibleBackupReminder()
    {
        var home = XDocument.Load(AppPath("Presentation", "Features", "Home", "HomePage.xaml"));
        var destination = XDocument.Load(AppPath("Presentation", "Features", "Home", "HomeDestinationPage.xaml"));
        AssertControlUsesCompiledBinding(home);
        AssertControlUsesCompiledBinding(destination);

        var source = File.ReadAllText(AppPath("Presentation", "Features", "Home", "HomePage.xaml"));
        foreach (var binding in new[]
                 {
                     "PeriodHeader.StartDateText",
                     "PeriodHeader.EndDateText",
                     "TotalText",
                     "TotalAccessibilityText",
                     "BasePayText",
                     "PremiumText",
                     "CountBonusText",
                     "AllowanceText",
                     "UncalculatedCountText",
                     "AnnualRangeText",
                     "AnnualTotalText",
                     "AnnualUncalculatedText",
                     "AnnualAccessibilityText",
                     "HasAnnualUncalculatedRecords",
                     "CalendarCommand",
                     "CalculationDetailsCommand",
                     "MonthlyAllowancesCommand",
                     "UncalculatedDaysCommand",
                     "BackupReminder.ShouldShow",
                     "BackupReminder.DeferCommand",
                 })
        {
            Assert.Contains(binding, source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("SemanticProperties.Description=\"給与算定開始日\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SemanticProperties.Description=\"給与算定終了日\"", source, StringComparison.Ordinal);
        Assert.Contains("SemanticProperties.Description=\"{Binding TotalAccessibilityText}\"", source, StringComparison.Ordinal);
        Assert.Contains("SemanticProperties.Description=\"{Binding AnnualAccessibilityText}\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationId=\"Home.AnnualSalarySummary\"", source, StringComparison.Ordinal);
        Assert.Contains("SemanticProperties.Description=\"{Binding PayrollPeriodAccessibilityText}\"", File.ReadAllText(AppPath("Presentation", "Features", "Home", "HomeDestinationPage.xaml")), StringComparison.Ordinal);

        var uncalculatedRecords = home.Descendants().Single(element =>
            AttributeValue(element, "AutomationId") == "Home.UncalculatedRecords");
        Assert.DoesNotContain(uncalculatedRecords.Attributes(), attribute => attribute.Name.LocalName == "IsVisible");
        Assert.Contains(uncalculatedRecords.Descendants(), element =>
            element.Name.LocalName == "Label" &&
            AttributeValue(element, "Text") == "{Binding UncalculatedCountText}");
        Assert.Contains(uncalculatedRecords.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            AttributeValue(element, "Text") == "対象日を見る" &&
            AttributeValue(element, "IsVisible") == "{Binding HasUncalculatedRecords}" &&
            AttributeValue(element, "IsEnabled") == "{Binding HasUncalculatedRecords}");

        Assert.Contains(home.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            AttributeValue(element, "Text") == "後で" &&
            AttributeValue(element, "SemanticProperties.Description").Contains("7日間", StringComparison.Ordinal));

        var navigator = File.ReadAllText(
            AppPath("Presentation", "Features", "Home", "ShellHomeNavigator.cs"));
        Assert.Contains("ShellNavigationQueryParameters", navigator, StringComparison.Ordinal);
        Assert.Contains("NavigationRoutes.CalculationDetails", navigator, StringComparison.Ordinal);
        Assert.Contains("NavigationRoutes.MonthlyAllowances", navigator, StringComparison.Ordinal);
        Assert.Contains("MonthlyAllowancePage.PayrollPeriodParameter", navigator, StringComparison.Ordinal);
        Assert.Contains("NavigationRoutes.UncalculatedDays", navigator, StringComparison.Ordinal);

        var settingsRoutes = File.ReadAllText(
            AppPath("Presentation", "Features", "Settings", "ShellSettingsNavigator.cs"));
        Assert.Contains("Routing.RegisterRoute(NavigationRoutes.MonthlyAllowances, typeof(MonthlyAllowancePage))", settingsRoutes, StringComparison.Ordinal);
        Assert.DoesNotContain("Routing.RegisterRoute(NavigationRoutes.MonthlyAllowances", navigator, StringComparison.Ordinal);
    }

    [Fact]
    public void CalculationDetailAndCopyDayExposeSpecifiedBreakdownsAndConfirmationEntry()
    {
        var calculation = XDocument.Load(AppPath("Presentation", "Features", "Home", "CalculationDetailPage.xaml"));
        AssertControlUsesCompiledBinding(calculation);
        var controls = calculation.Descendants().Select(element => element.Name.LocalName).ToArray();
        Assert.Single(controls, name => name == "CollectionView");
        Assert.DoesNotContain("ScrollView", controls);
        var attributes = calculation.Descendants().SelectMany(element => element.Attributes()).ToArray();
        Assert.DoesNotContain(attributes, attribute => attribute.Name.LocalName == "BindableLayout.ItemsSource");
        var calculationSource = File.ReadAllText(AppPath("Presentation", "Features", "Home", "CalculationDetailPage.xaml"));
        foreach (var binding in new[]
                 {
                     "StartDateText",
                     "EndDateText",
                     "Rows",
                     "DetailRowTemplateSelector",
                     "TotalLabel",
                     "ShowsPayrollPeriodBreakdown",
                     "HasPeriodUncalculated",
                     "HasDaySubtotal",
                     "AppliedRateText",
                     "SettingMonthText",
                     "MissingReasonText",
                 })
        {
            Assert.Contains(binding, calculationSource, StringComparison.Ordinal);
        }

        var daySource = File.ReadAllText(AppPath("Presentation", "Features", "Calendar", "DayPage.xaml"));
        Assert.Contains("ShowDetailsCommand", daySource, StringComparison.Ordinal);
        Assert.Contains("CopySourceDate", daySource, StringComparison.Ordinal);
        Assert.Contains("CopySourceMaximumDate", daySource, StringComparison.Ordinal);
        Assert.Contains("CopyDayCommand", daySource, StringComparison.Ordinal);

        var routes = File.ReadAllText(AppPath("Presentation", "Features", "Home", "ShellHomeNavigator.cs"));
        Assert.Contains("typeof(CalculationDetailPage)", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("Routing.RegisterRoute(NavigationRoutes.CalculationDetails, typeof(HomeDestinationPage))", routes, StringComparison.Ordinal);
    }

    private static string AppPath(params string[] segments) =>
        Path.Combine([RepositoryRoot, "src", "TkpSalaryCalculator.App", .. segments]);

    private static XDocument LoadControl(string fileName) =>
        XDocument.Load(AppPath("Presentation", "Controls", fileName));

    private static void AssertControlUsesCompiledBinding(XDocument document) =>
        Assert.Contains(document.Root!.Attributes(), attribute => attribute.Name.LocalName == "DataType");

    private static bool HasAttribute(XElement element, string localName) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == localName);

    private static string AttributeValue(XElement element, string localName) =>
        element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == localName)?.Value ?? string.Empty;

    private static string Property(XDocument project, string name) =>
        project.Descendants(name).Single().Value;
}
