using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.ValueObjects;
using TkpSalaryCalculator.Infrastructure.Sqlite;

namespace TkpSalaryCalculator.App.Navigation;

public enum AppRootKind
{
    InitialSetup,
    Main,
}

public sealed record AppRootNavigationRequest(AppRootKind RootKind, string? SetupStep);

public interface IAppRootNavigator
{
    Task SetRootAsync(AppRootNavigationRequest request, CancellationToken cancellationToken);
}

public interface IApplicationDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

public sealed class SqliteDatabaseInitializer(SqliteDatabase database) : IApplicationDatabaseInitializer
{
    private readonly SqliteDatabase database = database ?? throw new ArgumentNullException(nameof(database));

    public Task InitializeAsync(CancellationToken cancellationToken) => database.InitializeAsync(cancellationToken);
}

public interface IAppSessionState
{
    InitialSetupStateDto? InitialSetupState { get; set; }

    string SelectedRootRoute { get; set; }

    YearMonth CalendarMonth { get; set; }

    DateOnly? SelectedCalendarDate { get; set; }

    YearMonth SettingsMonth { get; set; }

    PayrollPeriodKey? PayrollPeriod { get; set; }

    long GetDataGeneration(AppDataChangeKind dependencies);

    void NotifyDataChanged(AppDataChangeKind changes);

    void ResetDataGenerations();
}

[Flags]
public enum AppDataChangeKind
{
    None = 0,
    WorkRecords = 1 << 0,
    Settings = 1 << 1,
    ClosingRules = 1 << 2,
    MonthlyAllowances = 1 << 3,
    BasicShifts = 1 << 4,
    BackupStatus = 1 << 5,
    All = WorkRecords | Settings | ClosingRules | MonthlyAllowances | BasicShifts | BackupStatus,
}

/// <summary>Android の一時的な画面再生成をまたいで、ルート選択と表示対象を保持します。</summary>
public sealed class AppSessionState(DateOnly localToday) : IAppSessionState
{
    private string selectedRootRoute = NavigationRoutes.Home;
    private readonly long[] dataGenerations = new long[6];
    private long nextDataGeneration;

    public InitialSetupStateDto? InitialSetupState { get; set; }

    public string SelectedRootRoute
    {
        get => selectedRootRoute;
        set => selectedRootRoute = NavigationRoutes.IsMainTab(value) ? value : NavigationRoutes.Home;
    }

    public YearMonth CalendarMonth { get; set; } = new(localToday.Year, localToday.Month);

    public DateOnly? SelectedCalendarDate { get; set; } = localToday;

    public YearMonth SettingsMonth { get; set; } = new(localToday.Year, localToday.Month);

    public PayrollPeriodKey? PayrollPeriod { get; set; }

    public long GetDataGeneration(AppDataChangeKind dependencies)
    {
        var generation = 0L;
        for (var index = 0; index < dataGenerations.Length; index++)
        {
            var kind = (AppDataChangeKind)(1 << index);
            if ((dependencies & kind) != 0)
                generation = Math.Max(generation, dataGenerations[index]);
        }

        return generation;
    }

    public void NotifyDataChanged(AppDataChangeKind changes)
    {
        if ((changes & ~AppDataChangeKind.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(changes));
        if (changes == AppDataChangeKind.None) return;

        var generation = Interlocked.Increment(ref nextDataGeneration);
        for (var index = 0; index < dataGenerations.Length; index++)
        {
            var kind = (AppDataChangeKind)(1 << index);
            if ((changes & kind) != 0)
                Volatile.Write(ref dataGenerations[index], generation);
        }
    }

    public void ResetDataGenerations() => NotifyDataChanged(AppDataChangeKind.All);
}

/// <summary>DB 初期化と初期設定状態の確認を終えてから、進入可能なルートだけを公開します。</summary>
public sealed class AppStartupCoordinator(
    IApplicationDatabaseInitializer database,
    IInitialSetupUseCase initialSetup,
    IPayrollPeriodSettingsUseCase payrollPeriods,
    IUtcClock clock,
    ILocalDateConverter localDates,
    IAppSessionState sessionState,
    IAppRootNavigator rootNavigator)
{
    private readonly IApplicationDatabaseInitializer database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly IInitialSetupUseCase initialSetup = initialSetup ?? throw new ArgumentNullException(nameof(initialSetup));
    private readonly IPayrollPeriodSettingsUseCase payrollPeriods = payrollPeriods ?? throw new ArgumentNullException(nameof(payrollPeriods));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILocalDateConverter localDates = localDates ?? throw new ArgumentNullException(nameof(localDates));
    private readonly IAppSessionState sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
    private readonly IAppRootNavigator rootNavigator = rootNavigator ?? throw new ArgumentNullException(nameof(rootNavigator));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var state = await initialSetup.GetStateAsync(cancellationToken).ConfigureAwait(false);
        sessionState.InitialSetupState = state;

        if (state.Status == InitialSetupStatus.Completed && sessionState.PayrollPeriod is null)
        {
            var localToday = localDates.ToLocalDate(clock.UtcNow);
            sessionState.PayrollPeriod = (await payrollPeriods.FindPeriodAsync(localToday, cancellationToken)
                .ConfigureAwait(false)).Key;
        }

        var request = state.Status == InitialSetupStatus.Completed
            ? new AppRootNavigationRequest(AppRootKind.Main, null)
            : new AppRootNavigationRequest(AppRootKind.InitialSetup, state.Step);

        await rootNavigator.SetRootAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
