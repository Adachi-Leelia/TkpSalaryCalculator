namespace TkpSalaryCalculator.App.Presentation.Services;

public interface IConfirmationDialogService
{
    Task<bool> ConfirmDiscardChangesAsync(CancellationToken cancellationToken = default);

    Task<bool> ConfirmAsync(
        string title,
        string message,
        string acceptText,
        string cancelText,
        CancellationToken cancellationToken = default);
}
