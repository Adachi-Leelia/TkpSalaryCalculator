using System.Windows.Input;

namespace TkpSalaryCalculator.App.Presentation.Common;

public sealed class AsyncCommand(
    Func<Task> execute,
    Action<Exception> onException,
    Func<bool>? canExecute = null) : ICommand
{
    private readonly Func<Task> execute = execute ?? throw new ArgumentNullException(nameof(execute));
    private readonly Func<bool>? canExecute = canExecute;
    private readonly Action<Exception> onException = onException ?? throw new ArgumentNullException(nameof(onException));
    private int isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => Volatile.Read(ref isExecuting) == 0 && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (Interlocked.CompareExchange(ref isExecuting, 1, 0) != 0) return;
        NotifyCanExecuteChanged();
        try
        {
            if (!(canExecute?.Invoke() ?? true)) return;
            await execute();
        }
        catch (Exception exception)
        {
            onException(exception);
        }
        finally
        {
            Interlocked.Exchange(ref isExecuting, 0);
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
