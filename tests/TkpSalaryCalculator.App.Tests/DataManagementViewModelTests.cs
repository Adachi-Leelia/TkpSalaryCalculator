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
        var viewModel = new DataManagementViewModel(
            transfers, new BackupReminderStub(), new DocumentStub(), new AppInformationStub(), new DialogStub(),
            notifications, root, new AppSessionState(new DateOnly(2026, 8, 21)), new ClockStub(), new LocalDateStub(),
            new UserErrorPresenter());
        root.OnSetRoot = viewModel.CancelPendingOperations;

        await viewModel.ImportAsync();

        Assert.True(transfers.Committed);
        Assert.Equal("インポート完了", notifications.Title);
        Assert.True(notifications.UsedLifetimeIndependentToken);
    }

    private sealed class TransferStub : IDataTransferUseCase
    {
        public bool Committed { get; private set; }
        public Task<DataTransferFormatDto> GetFormatAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ExportAsync(Stream destination, string appVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImportPreviewDto> PrepareImportAsync(Stream source, CancellationToken cancellationToken) => Task.FromResult(new ImportPreviewDto(
            new PreparedImportId(Guid.NewGuid()), 1, DateTimeOffset.UtcNow, 1, 0, 0, 0, null, null, null, null, []));
        public Task CommitImportAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken) { Committed = true; return Task.CompletedTask; }
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
        public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            Title = title;
            UsedLifetimeIndependentToken = !cancellationToken.CanBeCanceled;
            return Task.CompletedTask;
        }
    }

    private sealed class RootNavigatorStub : IAppRootNavigator
    {
        public Action? OnSetRoot { get; set; }
        public Task SetRootAsync(AppRootNavigationRequest request, CancellationToken cancellationToken)
        {
            OnSetRoot?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class ClockStub : IUtcClock { public DateTimeOffset UtcNow => new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero); }
    private sealed class LocalDateStub : ILocalDateConverter { public DateOnly ToLocalDate(DateTimeOffset utcDateTime) => new(2026, 8, 21); }
}
