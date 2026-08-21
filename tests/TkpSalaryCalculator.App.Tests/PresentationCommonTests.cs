namespace TkpSalaryCalculator.App.Tests;

public sealed class PresentationCommonTests
{
    [Fact]
    public void JapaneseFormatter_UsesSpecifiedLabelsAndFormats()
    {
        var formatter = new JapaneseDisplayFormatter();
        var period = new PayrollPeriod(
            new PayrollPeriodKey(new YearMonth(2026, 9)),
            new DateOnly(2026, 8, 21),
            new DateOnly(2026, 9, 20));

        Assert.Equal("1,234,567円", formatter.Money(new YenAmount(1_234_567)));
        Assert.Equal("2026年8月21日（金）", formatter.Date(new DateOnly(2026, 8, 21)));
        Assert.Equal("2026年8月", formatter.Month(new YearMonth(2026, 8)));
        Assert.Equal("設定対象年月: 2026年8月", formatter.SettingsMonth(new YearMonth(2026, 8)));
        Assert.Equal("09:05", formatter.Time(new MinuteOfDay(545)));
        Assert.Equal("45分", formatter.Duration(new WorkMinutes(45)));
        Assert.Equal("2時間", formatter.Duration(new WorkMinutes(120)));
        Assert.Equal("2時間5分", formatter.Duration(new WorkMinutes(125)));
        Assert.Equal(
            "給与算定開始日: 2026年8月21日\n給与算定終了日: 2026年9月20日",
            formatter.PayrollPeriod(period));
    }

    [Fact]
    public void IssuePresenter_SeparatesFieldAndScreenErrorsAndKeepsFirstField()
    {
        var presenter = new IssuePresenter();

        var result = presenter.Present([
            new IssueDto("RATE_REQUIRED", "HourlyWage", "時給を入力してください。"),
            new IssueDto("RATE_REQUIRED", "HourlyWage", "時給を入力してください。"),
            new IssueDto("TIME_ORDER", "EndTime", "終了時刻を確認してください。"),
            new IssueDto("SAVE_FAILED", null, "保存できませんでした。"),
        ]);

        Assert.Equal("HourlyWage", result.FirstInvalidField);
        Assert.Equal("時給を入力してください。", result.FieldErrors["HourlyWage"]);
        Assert.Equal("終了時刻を確認してください。", result.FieldErrors["EndTime"]);
        Assert.Equal("保存できませんでした。", result.ScreenMessage);
    }

    [Fact]
    public void UserErrorPresenter_ExposesOnlySafeMessages()
    {
        var presenter = new UserErrorPresenter();

        Assert.Equal(
            "入力を確認してください。",
            presenter.GetMessage(new ApplicationErrorException("INVALID", "入力を確認してください。")));
        Assert.Equal(
            "処理に失敗しました。入力内容を保持しています。もう一度お試しください。",
            presenter.GetMessage(new IOException("database path and internal details")));
    }

    [Fact]
    public async Task ViewModel_RunBusyTracksStateAndShowsApplicationError()
    {
        var viewModel = new TestViewModel(new UserErrorPresenter());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = viewModel.RunAsync(async _ =>
        {
            started.SetResult();
            await release.Task;
            throw new ApplicationErrorException("SAVE_FAILED", "保存できませんでした。");
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.IsNotBusy);
        release.SetResult();
        await running;

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.IsNotBusy);
        Assert.True(viewModel.HasError);
        Assert.Equal("保存できませんでした。", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ViewModel_CancelledPageWorkDoesNotShowAnError()
    {
        var viewModel = new TestViewModel(new UserErrorPresenter());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = viewModel.RunAsync(async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.CancelPendingOperations();
        await running;

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task AsyncCommand_PreventsDoubleExecutionAndRestoresAvailability()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var available = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Exception? observedException = null;
        var command = new AsyncCommand(async () =>
        {
            calls++;
            started.SetResult();
            await release.Task;
        }, exception => observedException = exception);
        command.CanExecuteChanged += (_, _) =>
        {
            if (command.CanExecute(null)) available.TrySetResult();
        };

        command.Execute(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        command.Execute(null);

        Assert.Equal(1, calls);
        Assert.False(command.CanExecute(null));
        release.SetResult();
        await available.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(command.CanExecute(null));
        Assert.Null(observedException);
    }

    [Fact]
    public async Task AsyncCommand_AlwaysForwardsUnexpectedException()
    {
        var observed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncCommand(
            () => Task.FromException(new InvalidOperationException("unexpected")),
            exception => observed.TrySetResult(exception));

        command.Execute(null);

        var exception = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task EditableViewModel_ConfirmsDirtyNavigationAndTargetChanges()
    {
        var dialogs = new DialogStub { Result = false };
        var viewModel = new EditableTestViewModel(new UserErrorPresenter(), dialogs);

        Assert.True(await viewModel.CanLeaveAsync());
        Assert.Equal(0, dialogs.DiscardCalls);

        viewModel.Dirty();
        Assert.False(await viewModel.CanLeaveAsync());
        Assert.Equal(1, dialogs.DiscardCalls);

        var changed = false;
        Assert.False(await viewModel.TryChangeTargetAsync(_ =>
        {
            changed = true;
            return Task.CompletedTask;
        }));
        Assert.False(changed);

        dialogs.Result = true;
        Assert.True(await viewModel.TryChangeTargetAsync(_ =>
        {
            changed = true;
            return Task.CompletedTask;
        }));
        Assert.True(changed);
    }

    private sealed class TestViewModel(IUserErrorPresenter presenter) : ViewModelBase(presenter)
    {
        public Task RunAsync(Func<CancellationToken, Task> operation) => RunBusyAsync(operation);
    }

    private sealed class EditableTestViewModel(
        IUserErrorPresenter presenter,
        IConfirmationDialogService dialogs) : EditableViewModelBase(presenter, dialogs)
    {
        public void Dirty() => MarkDirty();

        public Task<bool> TryChangeTargetAsync(Func<CancellationToken, Task> operation) =>
            RunAfterLeaveConfirmationAsync(operation);
    }

    private sealed class DialogStub : IConfirmationDialogService
    {
        public bool Result { get; set; }
        public int DiscardCalls { get; private set; }

        public Task<bool> ConfirmDiscardChangesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiscardCalls++;
            return Task.FromResult(Result);
        }

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string acceptText,
            string cancelText,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
