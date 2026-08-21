namespace TkpSalaryCalculator.App.Presentation.Services;

public sealed class ConfirmationDialogService : IConfirmationDialogService
{
    public Task<bool> ConfirmDiscardChangesAsync(CancellationToken cancellationToken = default) =>
        ConfirmAsync(
            "変更を破棄しますか",
            "保存していない変更があります。破棄すると元に戻せません。",
            "破棄する",
            "編集を続ける",
            cancellationToken);

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string acceptText,
        string cancelText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(acceptText);
        ArgumentException.ThrowIfNullOrWhiteSpace(cancelText);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = GetVisiblePage();
            return await page.DisplayAlertAsync(title, message, acceptText, cancelText);
        });
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static Page GetVisiblePage()
    {
        var root = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page
                   ?? throw new InvalidOperationException("確認画面を表示できません。");
        return root is Shell shell ? shell.CurrentPage : root;
    }
}
