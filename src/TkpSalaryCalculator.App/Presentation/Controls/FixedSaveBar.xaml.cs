using System.Windows.Input;

namespace TkpSalaryCalculator.App.Presentation.Controls;

public partial class FixedSaveBar : ContentView
{
    public static readonly BindableProperty SaveTextProperty = BindableProperty.Create(
        nameof(SaveText), typeof(string), typeof(FixedSaveBar), "保存");

    public static readonly BindableProperty SaveCommandProperty = BindableProperty.Create(
        nameof(SaveCommand), typeof(ICommand), typeof(FixedSaveBar));

    public static readonly BindableProperty IsSavingProperty = BindableProperty.Create(
        nameof(IsSaving), typeof(bool), typeof(FixedSaveBar), false, propertyChanged: OnSavingChanged);

    public FixedSaveBar() => InitializeComponent();

    public string SaveText
    {
        get => (string)GetValue(SaveTextProperty);
        set => SetValue(SaveTextProperty, value);
    }

    public ICommand? SaveCommand
    {
        get => (ICommand?)GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }

    public bool IsSaving
    {
        get => (bool)GetValue(IsSavingProperty);
        set => SetValue(IsSavingProperty, value);
    }

    public bool IsNotSaving => !IsSaving;

    private static void OnSavingChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((FixedSaveBar)bindable).OnPropertyChanged(nameof(IsNotSaving));
}
