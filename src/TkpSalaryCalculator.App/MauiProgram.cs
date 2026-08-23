using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Features.Calendar;
using TkpSalaryCalculator.App.Presentation.Features.DataManagement;
using TkpSalaryCalculator.App.Presentation.Features.Home;
using TkpSalaryCalculator.App.Presentation.Features.Settings;
using TkpSalaryCalculator.App.Presentation.Features.Setup;
using TkpSalaryCalculator.App.Presentation.Features.Startup;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Infrastructure.DataTransfer;
using TkpSalaryCalculator.Infrastructure.Sqlite;

namespace TkpSalaryCalculator.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var japaneseCulture = CultureInfo.GetCultureInfo("ja-JP");
        CultureInfo.DefaultThreadCurrentCulture = japaneseCulture;
        CultureInfo.DefaultThreadCurrentUICulture = japaneseCulture;

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(_ => { });

        RegisterInfrastructure(builder.Services);
        RegisterUseCases(builder.Services);
        RegisterPresentation(builder.Services);

        return builder.Build();
    }

    private static void RegisterInfrastructure(IServiceCollection services)
    {
        services.AddSingleton(_ => new SqliteInfrastructureOptions(
            Path.Combine(FileSystem.Current.AppDataDirectory, "tkp-salary-calculator.db3"),
            Path.Combine(FileSystem.Current.CacheDirectory, "import-staging")));
        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<SqliteInfrastructureOptions>();
            return new SqliteDatabase(options.DatabasePath);
        });

        services.AddSingleton<IUtcClock, SystemUtcClock>();
        services.AddSingleton<ILocalDateConverter>(_ => new TimeZoneLocalDateConverter(TimeZoneInfo.Local));
        services.AddSingleton<ITransactionRunner, SqliteTransactionRunner>();
        services.AddSingleton<IAppMetadataRepository, SqliteAppMetadataRepository>();
        services.AddSingleton<IServicePresetRepository, SqliteServicePresetRepository>();
        services.AddSingleton<IWorkRecordRepository, SqliteWorkRecordRepository>();
        services.AddSingleton<ISettingSnapshotRepository, SqliteSettingSnapshotRepository>();
        services.AddSingleton<IClosingRuleRepository, SqliteClosingRuleRepository>();
        services.AddSingleton<IMonthlyAllowanceRepository, SqliteMonthlyAllowanceRepository>();
        services.AddSingleton<IBasicShiftRepository, SqliteBasicShiftRepository>();
        services.AddSingleton<IHolidayCalendarRepository, SqliteHolidayCalendarRepository>();
        services.AddSingleton<IJsonExportStream, StreamingJsonExportStream>();
        services.AddSingleton<IJsonImportStream, StreamingJsonImportStream>();
        services.AddSingleton<IExportDataSource, SqliteExportDataSource>();
        services.AddSingleton<IImportStagingRepository>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<SqliteInfrastructureOptions>();
            return new SqliteImportStagingRepository(
                serviceProvider.GetRequiredService<SqliteDatabase>(),
                options.ImportStagingDirectory,
                serviceProvider.GetRequiredService<IUtcClock>());
        });
        services.AddSingleton<IApplicationDatabaseInitializer, SqliteDatabaseInitializer>();
    }

    private static void RegisterUseCases(IServiceCollection services)
    {
        services.AddSingleton<ISalaryCalculator, SalaryCalculator>();
        services.AddSingleton<IPayrollPeriodCalculator, PayrollPeriodCalculator>();
        services.AddSingleton<IInitialSetupUseCase, InitialSetupUseCase>();
        services.AddSingleton<IServicePresetUseCase, ServicePresetUseCase>();
        services.AddSingleton<IWorkRecordUseCase, WorkRecordUseCase>();
        services.AddSingleton<ISalaryQueryUseCase, SalaryQueryUseCase>();
        services.AddSingleton<IMonthSettingsUseCase, MonthSettingsUseCase>();
        services.AddSingleton<IPayrollPeriodSettingsUseCase, PayrollPeriodSettingsUseCase>();
        services.AddSingleton<IBackupReminderUseCase, BackupReminderUseCase>();
        services.AddSingleton<IBasicShiftUseCase, BasicShiftUseCase>();
        services.AddSingleton<IDataTransferUseCase, DataTransferUseCase>();
    }

    private static void RegisterPresentation(IServiceCollection services)
    {
        services.AddSingleton<IUserErrorPresenter, UserErrorPresenter>();
        services.AddSingleton<IssuePresenter>();
        services.AddSingleton<JapaneseDisplayFormatter>();
        services.AddSingleton<IAppSessionState>(serviceProvider =>
        {
            var clock = serviceProvider.GetRequiredService<IUtcClock>();
            var localDates = serviceProvider.GetRequiredService<ILocalDateConverter>();
            return new AppSessionState(localDates.ToLocalDate(clock.UtcNow));
        });
        services.AddSingleton<AppRootNavigator>();
        services.AddSingleton<IAppRootNavigator>(serviceProvider => serviceProvider.GetRequiredService<AppRootNavigator>());
        services.AddSingleton<AppStartupCoordinator>();
        services.AddSingleton<IConfirmationDialogService, ConfirmationDialogService>();
        services.AddSingleton<IPlatformDocumentService, AndroidDocumentService>();
        services.AddSingleton<IAppInformationService, MauiAppInformationService>();
        services.AddSingleton<IUserNotificationService, UserNotificationService>();
        services.AddSingleton<IHomeNavigator, ShellHomeNavigator>();
        services.AddSingleton<ICalendarNavigator, ShellCalendarNavigator>();
        services.AddSingleton<ISettingsNavigator, ShellSettingsNavigator>();
        services.AddSingleton<SettingsMonthContext>();

        services.AddSingleton<StartupViewModel>();
        services.AddSingleton<StartupPage>();
        services.AddTransient<InitialSetupFlowViewModel>();
        services.AddTransient<InitialSetupFlowPage>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<HomePage>();
        services.AddTransient<CalculationDetailViewModel>();
        services.AddTransient<CalculationDetailPage>();
        services.AddTransient<CalendarViewModel>();
        services.AddTransient<CalendarPage>();
        services.AddTransient<DayViewModel>();
        services.AddTransient<DayPage>();
        services.AddTransient<WorkEditorViewModel>();
        services.AddTransient<WorkEditorPage>();
        services.AddTransient<SettingsMenuViewModel>();
        services.AddTransient<SettingsMenuPage>();
        services.AddTransient<ServiceSettingsViewModel>();
        services.AddTransient<ServiceSettingsPage>();
        services.AddTransient<ServiceSettingsEditorViewModel>();
        services.AddTransient<ServiceSettingsEditorPage>();
        services.AddTransient<PremiumSettingsViewModel>();
        services.AddTransient<PremiumSettingsPage>();
        services.AddTransient<PremiumSettingsEditorViewModel>();
        services.AddTransient<PremiumSettingsEditorPage>();
        services.AddTransient<CountBonusSettingsViewModel>();
        services.AddTransient<CountBonusSettingsPage>();
        services.AddTransient<CountBonusSettingsEditorViewModel>();
        services.AddTransient<CountBonusSettingsEditorPage>();
        services.AddTransient<PayrollPeriodSettingsViewModel>();
        services.AddTransient<PayrollPeriodSettingsPage>();
        services.AddTransient<MonthlyAllowanceViewModel>();
        services.AddTransient<MonthlyAllowancePage>();
        services.AddTransient<MonthlyAllowanceEditorViewModel>();
        services.AddTransient<MonthlyAllowanceEditorPage>();
        services.AddTransient<BasicShiftViewModel>();
        services.AddTransient<BasicShiftPage>();
        services.AddTransient<BasicShiftEditorViewModel>();
        services.AddTransient<BasicShiftEditorPage>();
        services.AddTransient<DataManagementViewModel>();
        services.AddTransient<DataManagementPage>();
        services.AddTransient<AppInformationViewModel>();
        services.AddTransient<AppInformationPage>();
    }
}
