namespace TkpSalaryCalculator.App.Presentation.Features.Settings;
public partial class PremiumSettingsEditorPage : ContentPage, IQueryAttributable
{
    public const string IdParameter = "premiumId"; private readonly PremiumSettingsEditorViewModel viewModel;
    public PremiumSettingsEditorPage(PremiumSettingsEditorViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    public void ApplyQueryAttributes(IDictionary<string, object> query) => viewModel.Initialize(query.TryGetValue(IdParameter, out var value) && Guid.TryParse(value?.ToString(), out var id) ? id : null);
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadIfNeededAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
}
