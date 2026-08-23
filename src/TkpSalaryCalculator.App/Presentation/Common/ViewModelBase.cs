using TkpSalaryCalculator.App.Navigation;

namespace TkpSalaryCalculator.App.Presentation.Common;

public abstract class ViewModelBase(IUserErrorPresenter errorPresenter) : ObservableObject
{
    private readonly IUserErrorPresenter errorPresenter = errorPresenter ?? throw new ArgumentNullException(nameof(errorPresenter));
    private CancellationTokenSource? currentOperation;
    private bool isBusy;
    private string? errorMessage;
    private IAppSessionState? trackedSessionState;
    private AppDataChangeKind trackedDependencies;
    private long? lastLoadedGeneration;

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
        await TryRunBusyAsync(operation);
    }

    protected async Task<bool> TryRunBusyAsync(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (IsBusy) return false;

        using var source = new CancellationTokenSource();
        currentOperation = source;
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await operation(source.Token);
            return true;
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            // A disappearing page cancels obsolete work without showing an error.
            return false;
        }
        catch (Exception exception)
        {
            PresentError(exception);
            return false;
        }
        finally
        {
            if (ReferenceEquals(currentOperation, source)) currentOperation = null;
            IsBusy = false;
        }
    }

    protected void TrackDataChanges(IAppSessionState sessionState, AppDataChangeKind dependencies)
    {
        trackedSessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        if (dependencies == AppDataChangeKind.None || (dependencies & ~AppDataChangeKind.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(dependencies));
        trackedDependencies = dependencies;
    }

    protected async Task LoadTrackedAsync(Func<CancellationToken, Task> operation, bool force)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var sessionState = trackedSessionState ??
            throw new InvalidOperationException("変更世代を追跡するセッションが設定されていません。");
        var generation = sessionState.GetDataGeneration(trackedDependencies);
        if (!force && lastLoadedGeneration == generation) return;

        if (await TryRunBusyAsync(operation))
            lastLoadedGeneration = generation;
    }

    protected bool IsTrackedDataCurrent()
    {
        var sessionState = trackedSessionState ??
            throw new InvalidOperationException("変更世代を追跡するセッションが設定されていません。");
        return lastLoadedGeneration == sessionState.GetDataGeneration(trackedDependencies);
    }

    protected long CaptureTrackedDataGeneration()
    {
        var sessionState = trackedSessionState ??
            throw new InvalidOperationException("変更世代を追跡するセッションが設定されていません。");
        return sessionState.GetDataGeneration(trackedDependencies);
    }

    protected void AcceptDataGeneration(long generation) => lastLoadedGeneration = generation;

    protected void InvalidateTrackedLoad() => lastLoadedGeneration = null;

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
