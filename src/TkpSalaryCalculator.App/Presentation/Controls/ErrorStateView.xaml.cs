using System.Windows.Input;

namespace TkpSalaryCalculator.App.Presentation.Controls;

public partial class ErrorStateView : ContentView
{
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message), typeof(string), typeof(ErrorStateView),
        "入力内容は保持されています。もう一度お試しください。");

    public static readonly BindableProperty RetryCommandProperty = BindableProperty.Create(
        nameof(RetryCommand), typeof(ICommand), typeof(ErrorStateView), propertyChanged: OnRetryChanged);

    public ErrorStateView() => InitializeComponent();

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => (ICommand?)GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    public bool HasRetry => RetryCommand is not null;

    private static void OnRetryChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((ErrorStateView)bindable).OnPropertyChanged(nameof(HasRetry));
}
