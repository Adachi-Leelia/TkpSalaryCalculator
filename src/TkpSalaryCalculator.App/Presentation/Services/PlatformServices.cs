namespace TkpSalaryCalculator.App.Presentation.Services;

public sealed class MauiAppInformationService : IAppInformationService
{
    public string Name => AppInfo.Current.Name;

    public string DisplayVersion => AppInfo.Current.VersionString;

    public string BuildNumber => AppInfo.Current.BuildString;
}

public sealed class UserNotificationService : IUserNotificationService
{
    public async Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = Shell.Current?.CurrentPage
                ?? Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page
                ?? throw new InvalidOperationException("メッセージを表示できません。");
            await page.DisplayAlertAsync(title, message, "OK");
        });
    }
}
