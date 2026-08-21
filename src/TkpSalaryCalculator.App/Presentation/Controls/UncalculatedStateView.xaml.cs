namespace TkpSalaryCalculator.App.Presentation.Controls;

public partial class UncalculatedStateView : ContentView
{
    public static readonly BindableProperty ReasonProperty = BindableProperty.Create(
        nameof(Reason), typeof(string), typeof(UncalculatedStateView), "給与設定が不足しています。");

    public static readonly BindableProperty NextActionProperty = BindableProperty.Create(
        nameof(NextAction), typeof(string), typeof(UncalculatedStateView), "設定画面で不足している項目を入力してください。");

    public UncalculatedStateView() => InitializeComponent();

    public string Reason
    {
        get => (string)GetValue(ReasonProperty);
        set => SetValue(ReasonProperty, value);
    }

    public string NextAction
    {
        get => (string)GetValue(NextActionProperty);
        set => SetValue(NextActionProperty, value);
    }
}
