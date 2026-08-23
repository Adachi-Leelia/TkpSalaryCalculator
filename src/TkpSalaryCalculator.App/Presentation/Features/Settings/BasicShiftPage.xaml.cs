namespace TkpSalaryCalculator.App.Presentation.Features.Settings;
public partial class BasicShiftPage : ContentPage, IQueryAttributable
{
    private readonly BasicShiftViewModel viewModel;
    public BasicShiftPage(BasicShiftViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    public void ApplyQueryAttributes(IDictionary<string, object> query) { if (query.TryGetValue(SettingsPageQuery.SuccessMessageParameter, out var value)) viewModel.SetSuccessMessage(value?.ToString()); }
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadIfNeededAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
}
