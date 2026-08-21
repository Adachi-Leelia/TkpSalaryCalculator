using System.Windows.Input;

namespace TkpSalaryCalculator.App.Presentation.Controls;

public partial class EmptyStateView : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(EmptyStateView), "データがありません。");

    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message), typeof(string), typeof(EmptyStateView), string.Empty);

    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(
        nameof(ActionText), typeof(string), typeof(EmptyStateView), string.Empty, propertyChanged: OnActionChanged);

    public static readonly BindableProperty ActionCommandProperty = BindableProperty.Create(
        nameof(ActionCommand), typeof(ICommand), typeof(EmptyStateView), propertyChanged: OnActionChanged);

    public EmptyStateView() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public bool HasAction => ActionCommand is not null && !string.IsNullOrWhiteSpace(ActionText);

    private static void OnActionChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((EmptyStateView)bindable).OnPropertyChanged(nameof(HasAction));
}
