using System.ComponentModel;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

public partial class ServiceSettingsEditorPage : ContentPage, IQueryAttributable
{
    public const string IdParameter = "serviceSettingId";
    public const string ModeParameter = "serviceSettingMode";
    private readonly ServiceSettingsEditorViewModel viewModel;
    public ServiceSettingsEditorPage(ServiceSettingsEditorViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
        this.viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var mode = query.TryGetValue(ModeParameter, out var modeValue) &&
            Enum.TryParse<ServiceSettingsEditorMode>(modeValue?.ToString(), out var parsedMode)
                ? parsedMode
                : ServiceSettingsEditorMode.AddService;
        var id = query.TryGetValue(IdParameter, out var idValue) && Guid.TryParse(idValue?.ToString(), out var parsedId)
            ? parsedId
            : (Guid?)null;
        viewModel.Initialize(mode, id);
    }
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadIfNeededAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(ServiceSettingsEditorViewModel.FirstInvalidField) ||
            string.IsNullOrWhiteSpace(viewModel.FirstInvalidField)) return;
        var target = viewModel.FirstInvalidField switch
        {
            nameof(ServiceSettingsEditorViewModel.ServiceName) => ServiceNameEntry,
            nameof(ServiceSettingsEditorViewModel.CategoryName) => CategoryNameEntry,
            nameof(ServiceSettingsEditorViewModel.CategoryStandardMinutesText) => CategoryStandardMinutesEntry,
            nameof(ServiceSettingsEditorViewModel.ServiceDisplayOrderText) => ServiceDisplayOrderEntry,
            nameof(ServiceSettingsEditorViewModel.CategoryDisplayOrderText) => CategoryDisplayOrderEntry,
            nameof(ServiceSettingsEditorViewModel.AmountText) => AmountEntry,
            nameof(ServiceSettingsEditorViewModel.CandidateName) => CandidateNameEntry,
            nameof(ServiceSettingsEditorViewModel.CandidateDefaultMinutesText) => CandidateDefaultMinutesEntry,
            nameof(ServiceSettingsEditorViewModel.CandidateOrderText) => CandidateOrderEntry,
            _ => null,
        };
        Dispatcher.Dispatch(() => _ = SettingsEditorFocus.FocusAsync(EditorScroll, target));
    }
}
