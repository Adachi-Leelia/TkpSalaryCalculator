using TkpSalaryCalculator.App.Presentation.Services;

namespace TkpSalaryCalculator.App.Presentation.Common;

public interface ILeaveGuard
{
    Task<bool> CanLeaveAsync(CancellationToken cancellationToken = default);
}

public abstract class EditableViewModelBase(
    IUserErrorPresenter errorPresenter,
    IConfirmationDialogService dialogs) : ViewModelBase(errorPresenter), ILeaveGuard
{
    private readonly IConfirmationDialogService dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    private bool isDirty;

    public bool IsDirty
    {
        get => isDirty;
        protected set => SetProperty(ref isDirty, value);
    }

    public async Task<bool> CanLeaveAsync(CancellationToken cancellationToken = default)
    {
        if (!IsDirty) return true;
        return await dialogs.ConfirmDiscardChangesAsync(cancellationToken);
    }

    protected async Task<bool> RunAfterLeaveConfirmationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!await CanLeaveAsync(cancellationToken)) return false;
        await operation(cancellationToken);
        return true;
    }

    protected void MarkDirty() => IsDirty = true;

    protected void MarkSaved() => IsDirty = false;
}
