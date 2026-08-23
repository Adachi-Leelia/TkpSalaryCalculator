namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

/// <summary>SCR-SET-01 の設定メニューです。</summary>
public partial class SettingsMenuPage : ContentPage, IQueryAttributable
{
    private readonly SettingsMenuViewModel viewModel;

    public SettingsMenuPage(SettingsMenuViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(SettingsPageQuery.SuccessMessageParameter, out var value))
            viewModel.SetSuccessMessage(value?.ToString());
    }

    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
}
