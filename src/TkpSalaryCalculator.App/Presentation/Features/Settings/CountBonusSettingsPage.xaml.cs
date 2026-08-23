namespace TkpSalaryCalculator.App.Presentation.Features.Settings;
public partial class CountBonusSettingsPage : ContentPage, IQueryAttributable
{
    private readonly CountBonusSettingsViewModel viewModel;
    public CountBonusSettingsPage(CountBonusSettingsViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    public void ApplyQueryAttributes(IDictionary<string, object> query) { if (query.TryGetValue(SettingsPageQuery.SuccessMessageParameter, out var value)) viewModel.SetSuccessMessage(value?.ToString()); }
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
    private async void OnEditClicked(object? sender, EventArgs e) { if (sender is Button { CommandParameter: CountBonusSettingRow row }) await viewModel.OpenEditorAsync(row); }
}
