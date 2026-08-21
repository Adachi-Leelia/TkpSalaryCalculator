using System.Windows.Input;
using TkpSalaryCalculator.App.Presentation.Common;

namespace TkpSalaryCalculator.App.Presentation.Features.Startup;

public sealed class StartupViewModel : ViewModelBase
{
    private Func<CancellationToken, Task>? startupOperation;

    public StartupViewModel(IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        RetryCommand = new AsyncCommand(StartAsync, PresentError);
    }

    public ICommand RetryCommand { get; }

    public void SetStartupOperation(Func<CancellationToken, Task> operation) =>
        startupOperation = operation ?? throw new ArgumentNullException(nameof(operation));

    public Task StartAsync() => RunBusyAsync(token =>
        (startupOperation ?? throw new InvalidOperationException("起動処理が準備されていません。"))(token));
}
