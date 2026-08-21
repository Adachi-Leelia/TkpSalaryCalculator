namespace TkpSalaryCalculator.App.Navigation;

public sealed class AppRootNavigator(IServiceProvider services, IAppSessionState sessionState) : IAppRootNavigator
{
    private readonly IServiceProvider services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly IAppSessionState sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
    private Window? window;

    public void Attach(Window appWindow) => window = appWindow ?? throw new ArgumentNullException(nameof(appWindow));

    public async Task SetRootAsync(AppRootNavigationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var attachedWindow = window ?? throw new InvalidOperationException("アプリケーションウィンドウが準備されていません。");

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            attachedWindow.Page = new AppShell(services, request.RootKind, sessionState);
        });
    }
}
