namespace TkpSalaryCalculator.App.Presentation.Common;

public abstract class ViewModelBase(IUserErrorPresenter errorPresenter) : ObservableObject
{
    private readonly IUserErrorPresenter errorPresenter = errorPresenter ?? throw new ArgumentNullException(nameof(errorPresenter));
    private CancellationTokenSource? currentOperation;
    private bool isBusy;
    private string? errorMessage;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value)) return;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string? ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (!SetProperty(ref errorMessage, value)) return;
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    protected async Task RunBusyAsync(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (IsBusy) return;

        using var source = new CancellationTokenSource();
        currentOperation = source;
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await operation(source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            // A disappearing page cancels obsolete work without showing an error.
        }
        catch (Exception exception)
        {
            PresentError(exception);
        }
        finally
        {
            if (ReferenceEquals(currentOperation, source)) currentOperation = null;
            IsBusy = false;
        }
    }

    public void ClearError() => ErrorMessage = null;

    public void CancelPendingOperations() => currentOperation?.Cancel();

    protected void PresentError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ErrorMessage = errorPresenter.GetMessage(exception);
        OnErrorPresented(exception);
    }

    protected virtual void OnErrorPresented(Exception exception)
    {
    }
}
