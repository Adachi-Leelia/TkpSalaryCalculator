using System.ComponentModel;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;
public partial class PremiumSettingsEditorPage : ContentPage, IQueryAttributable
{
    public const string IdParameter = "premiumId"; private readonly PremiumSettingsEditorViewModel viewModel;
    public PremiumSettingsEditorPage(PremiumSettingsEditorViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; this.viewModel.PropertyChanged += OnViewModelPropertyChanged; }
    public void ApplyQueryAttributes(IDictionary<string, object> query) => viewModel.Initialize(query.TryGetValue(IdParameter, out var value) && Guid.TryParse(value?.ToString(), out var id) ? id : null);
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadIfNeededAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(PremiumSettingsEditorViewModel.FirstInvalidField) || string.IsNullOrWhiteSpace(viewModel.FirstInvalidField)) return;
        VisualElement? target = viewModel.FirstInvalidField switch
        {
            nameof(PremiumSettingsEditorViewModel.DisplayName) => DisplayNameEntry,
            nameof(PremiumSettingsEditorViewModel.ValueText) => ValueEntry,
            nameof(PremiumSettingsEditorViewModel.IndividualDatesText) => IndividualDatesEntry,
            nameof(PremiumSettingsEditorViewModel.EndTime) => EndTimePicker,
            nameof(PremiumSettingsEditorViewModel.AppliesToAllServices) => AllServicesSwitch,
            _ => null,
        };
        Dispatcher.Dispatch(() => _ = SettingsEditorFocus.FocusAsync(EditorScroll, target));
    }
}
