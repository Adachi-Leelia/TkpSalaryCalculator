using System.ComponentModel;
using TkpSalaryCalculator.Domain.ValueObjects;
namespace TkpSalaryCalculator.App.Presentation.Features.Settings;
public partial class BasicShiftEditorPage : ContentPage, IQueryAttributable
{
    public const string IdParameter = "basicShiftId"; private readonly BasicShiftEditorViewModel viewModel;
    public BasicShiftEditorPage(BasicShiftEditorViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; this.viewModel.PropertyChanged += OnViewModelPropertyChanged; }
    public void ApplyQueryAttributes(IDictionary<string, object> query) => viewModel.Initialize(query.TryGetValue(IdParameter, out var value) && Guid.TryParse(value?.ToString(), out var id) ? new BasicShiftId(id) : null);
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadIfNeededAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(BasicShiftEditorViewModel.FirstInvalidField) ||
            string.IsNullOrWhiteSpace(viewModel.FirstInvalidField)) return;
        VisualElement? target = viewModel.FirstInvalidField switch
        {
            "ServiceId" => ServicePicker,
            "TimeCategoryId" => TimeCategoryPicker,
            "WorkMinutes" => WorkMinutesEntry,
            "StartTime" => viewModel.ShowTimeRange ? TimeRangeStartTimePicker : DurationStartTimePicker,
            "EndTime" => EndTimePicker,
            "DisplayOrder" => DisplayOrderEntry,
            _ => null,
        };
        Dispatcher.Dispatch(() => _ = SettingsEditorFocus.FocusAsync(EditorScroll, target));
    }
}
