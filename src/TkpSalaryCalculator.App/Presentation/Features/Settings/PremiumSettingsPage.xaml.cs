namespace TkpSalaryCalculator.App.Presentation.Features.Settings;
public partial class PremiumSettingsPage : ContentPage, IQueryAttributable
{
    private readonly PremiumSettingsViewModel viewModel;
    public PremiumSettingsPage(PremiumSettingsViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    public void ApplyQueryAttributes(IDictionary<string, object> query) { if (query.TryGetValue(SettingsPageQuery.SuccessMessageParameter, out var value)) viewModel.SetSuccessMessage(value?.ToString()); }
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadIfNeededAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
    private async void OnEditClicked(object? sender, EventArgs e) { if (sender is Button { CommandParameter: PremiumSettingRow row }) await viewModel.OpenEditorAsync(row); }
}
