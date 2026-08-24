using TkpSalaryCalculator.App.Presentation.Features.DataManagement;

namespace TkpSalaryCalculator.App.Tests;

public sealed class DataManagementViewModelTests
{
    [Fact]
    public async Task UI012_ImportCompletionNotificationUsesLifetimeIndependentTokenAfterRootReplacement()
    {
        var transfers = new TransferStub();
        var root = new RootNavigatorStub();
        var notifications = new NotificationStub();
        var session = new AppSessionState(new DateOnly(2026, 8, 21))
        {
            InitialSetupState = new InitialSetupStateDto(InitialSetupStatus.Completed, null, []),
        };
        var generationBefore = session.GetDataGeneration(AppDataChangeKind.All);
        var viewModel = new DataManagementViewModel(
            transfers, new BackupReminderStub(), new DocumentStub(), new AppInformationStub(), new DialogStub(),
            notifications, root, session, new ClockStub(), new LocalDateStub(),
            new UserErrorPresenter());
        root.OnSetRoot = viewModel.CancelPendingOperations;

        await viewModel.ImportAsync();

        Assert.True(transfers.Committed);
        Assert.Equal(1, root.SetRootCalls);
        Assert.True(root.UsedLifetimeIndependentToken);
        Assert.Equal("インポート完了", notifications.Title);
        Assert.True(notifications.UsedLifetimeIndependentToken);
        Assert.True(session.GetDataGeneration(AppDataChangeKind.All) > generationBefore);
        Assert.Equal(NavigationRoutes.Home, session.SelectedRootRoute);
        Assert.Null(session.InitialSetupState);
    }

    [Fact]
    public async Task ImportFailureBeforeCompletionDoesNotResetSessionOrRoot()
    {
        var transfers = new TransferStub { CommitFailure = new IOException("bootstrap") };
        var root = new RootNavigatorStub();
        var session = new AppSessionState(new DateOnly(2026, 8, 21))
        {
            InitialSetupState = new InitialSetupStateDto(InitialSetupStatus.Completed, null, []),
            SelectedRootRoute = NavigationRoutes.Calendar,
            PayrollPeriod = new PayrollPeriodKey(new YearMonth(2026, 8)),
        };
        var generationBefore = session.GetDataGeneration(AppDataChangeKind.All);
        var viewModel = new DataManagementViewModel(
            transfers, new BackupReminderStub(), new DocumentStub(), new AppInformationStub(), new DialogStub(),
            new NotificationStub(), root, session, new ClockStub(), new LocalDateStub(),
            new UserErrorPresenter());

        await viewModel.ImportAsync();

        Assert.Equal(0, root.SetRootCalls);
        Assert.Equal(generationBefore, session.GetDataGeneration(AppDataChangeKind.All));
        Assert.Equal(NavigationRoutes.Calendar, session.SelectedRootRoute);
        Assert.NotNull(session.PayrollPeriod);
        Assert.NotNull(session.InitialSetupState);
        Assert.True(viewModel.HasError);
    }

    [Fact]
    public async Task ImportRootReplacementFailureReportsCommittedImportAndRestartGuidance()
    {
        var transfers = new TransferStub();
        var root = new RootNavigatorStub { Failure = new InvalidOperationException("root") };
        var notifications = new NotificationStub();
        var session = new AppSessionState(new DateOnly(2026, 8, 21))
        {
            InitialSetupState = new InitialSetupStateDto(InitialSetupStatus.Completed, null, []),
            SelectedRootRoute = NavigationRoutes.Calendar,
        };
        var viewModel = new DataManagementViewModel(
            transfers, new BackupReminderStub(), new DocumentStub(), new AppInformationStub(), new DialogStub(),
            notifications, root, session, new ClockStub(), new LocalDateStub(),
            new UserErrorPresenter());

        await viewModel.ImportAsync();

        Assert.True(transfers.Committed);
        Assert.Equal(1, root.SetRootCalls);
        Assert.Equal(NavigationRoutes.Home, session.SelectedRootRoute);
        Assert.Null(session.InitialSetupState);
        Assert.Equal(
            "インポートは完了しましたが、画面を最新の状態へ更新できませんでした。アプリを再起動してください。",
            viewModel.ErrorMessage);
        Assert.Null(notifications.Title);
    }

    [Fact]
    public async Task ImportCompletionNotificationFailureDoesNotRelabelCommittedImportAsFailure()
    {
        var transfers = new TransferStub();
        var root = new RootNavigatorStub();
        var notifications = new NotificationStub { Failure = new InvalidOperationException("notification") };
        var session = new AppSessionState(new DateOnly(2026, 8, 21));
        var viewModel = new DataManagementViewModel(
            transfers, new BackupReminderStub(), new DocumentStub(), new AppInformationStub(), new DialogStub(),
            notifications, root, session, new ClockStub(), new LocalDateStub(),
            new UserErrorPresenter());

        await viewModel.ImportAsync();

        Assert.True(transfers.Committed);
        Assert.Equal(1, root.SetRootCalls);
        Assert.False(viewModel.HasError);
    }

    private sealed class TransferStub : IDataTransferUseCase
    {
        public bool Committed { get; private set; }
        public Exception? CommitFailure { get; init; }
        public Task<DataTransferFormatDto> GetFormatAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ExportAsync(Stream destination, string appVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImportPreviewDto> PrepareImportAsync(Stream source, CancellationToken cancellationToken) => Task.FromResult(new ImportPreviewDto(
            new PreparedImportId(Guid.NewGuid()), 1, DateTimeOffset.UtcNow, 1, 0, 0, 0, null, null, null, null, []));
        public Task CommitImportAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken)
        {
            if (CommitFailure is not null) return Task.FromException(CommitFailure);
            Committed = true;
            return Task.CompletedTask;
        }
        public Task DiscardImportAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class BackupReminderStub : IBackupReminderUseCase
    {
        public Task<BackupReminderStateDto> GetStateAsync(DateOnly localToday, CancellationToken cancellationToken) =>
            Task.FromResult(new BackupReminderStateDto(localToday, false, false, null, null, null));
        public Task<BackupReminderStateDto> DeferForSevenDaysAsync(DateOnly localToday, CancellationToken cancellationToken) =>
            Task.FromResult(new BackupReminderStateDto(localToday, false, false, null, null, null));
    }

    private sealed class DocumentStub : IPlatformDocumentService
    {
        public Task<Stream?> CreateExportAsync(string suggestedFileName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream?> OpenImportAsync(CancellationToken cancellationToken) => Task.FromResult<Stream?>(new MemoryStream([1]));
    }

    private sealed class AppInformationStub : IAppInformationService
    {
        public string Name => "test";
        public string DisplayVersion => "1.0";
        public string BuildNumber => "1";
    }

    private sealed class DialogStub : IConfirmationDialogService
    {
        public Task<bool> ConfirmDiscardChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancelText, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class NotificationStub : IUserNotificationService
    {
        public string? Title { get; private set; }
        public bool UsedLifetimeIndependentToken { get; private set; }
        public Exception? Failure { get; init; }
        public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            Title = title;
            UsedLifetimeIndependentToken = !cancellationToken.CanBeCanceled;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class RootNavigatorStub : IAppRootNavigator
    {
        public Action? OnSetRoot { get; set; }
        public int SetRootCalls { get; private set; }
        public bool UsedLifetimeIndependentToken { get; private set; }
        public Exception? Failure { get; init; }
        public Task SetRootAsync(AppRootNavigationRequest request, CancellationToken cancellationToken)
        {
            SetRootCalls++;
            UsedLifetimeIndependentToken = !cancellationToken.CanBeCanceled;
            OnSetRoot?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class ClockStub : IUtcClock { public DateTimeOffset UtcNow => new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero); }
    private sealed class LocalDateStub : ILocalDateConverter { public DateOnly ToLocalDate(DateTimeOffset utcDateTime) => new(2026, 8, 21); }
}
