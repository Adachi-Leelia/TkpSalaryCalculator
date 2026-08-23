namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

public partial class ServiceSettingsPage : ContentPage, IQueryAttributable
{
    private readonly ServiceSettingsViewModel viewModel;
    public ServiceSettingsPage(ServiceSettingsViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    public void ApplyQueryAttributes(IDictionary<string, object> query) { if (query.TryGetValue(SettingsPageQuery.SuccessMessageParameter, out var value)) viewModel.SetSuccessMessage(value?.ToString()); }
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadIfNeededAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
    private async void OnEditClicked(object? sender, EventArgs eventArgs) { if (sender is Button { CommandParameter: ServiceSettingRow row }) await viewModel.OpenEditorAsync(row); }
    private async void OnCandidateEditClicked(object? sender, EventArgs eventArgs) { if (sender is Button { CommandParameter: ServicePresetRow row }) await viewModel.OpenCandidateEditorAsync(row); }
}
