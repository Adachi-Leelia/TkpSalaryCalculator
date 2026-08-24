using System.ComponentModel;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;
public partial class CountBonusSettingsEditorPage : ContentPage, IQueryAttributable
{
    public const string IdParameter = "countBonusId"; private readonly CountBonusSettingsEditorViewModel viewModel;
    public CountBonusSettingsEditorPage(CountBonusSettingsEditorViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; this.viewModel.PropertyChanged += OnViewModelPropertyChanged; }
    public void ApplyQueryAttributes(IDictionary<string, object> query) => viewModel.Initialize(query.TryGetValue(IdParameter, out var value) && Guid.TryParse(value?.ToString(), out var id) ? id : null);
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadIfNeededAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(CountBonusSettingsEditorViewModel.FirstInvalidField) || string.IsNullOrWhiteSpace(viewModel.FirstInvalidField)) return;
        VisualElement? target = viewModel.FirstInvalidField switch
        {
            nameof(CountBonusSettingsEditorViewModel.DisplayName) => DisplayNameEntry,
            nameof(CountBonusSettingsEditorViewModel.AmountText) => AmountEntry,
            nameof(CountBonusSettingsEditorViewModel.AppliesToAllServices) => AllServicesSwitch,
            _ => null,
        };
        Dispatcher.Dispatch(() => _ = SettingsEditorFocus.FocusAsync(EditorScroll, target));
    }
}
