namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

public partial class ServiceSettingsEditorPage : ContentPage, IQueryAttributable
{
    public const string IdParameter = "serviceSettingId";
    private readonly ServiceSettingsEditorViewModel viewModel;
    public ServiceSettingsEditorPage(ServiceSettingsEditorViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    public void ApplyQueryAttributes(IDictionary<string, object> query) => viewModel.Initialize(query.TryGetValue(IdParameter, out var value) && Guid.TryParse(value?.ToString(), out var id) ? id : null);
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadIfNeededAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
}
