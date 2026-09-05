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
        Dispatcher.Dispatch(() => _ = FocusFirstInvalidFieldAsync());
    }

    private async Task FocusFirstInvalidFieldAsync()
    {
        if (viewModel.FirstInvalidField == "DisplayOrder")
        {
            await SettingsEditorFocus.FocusAsync(EditorScroll, DisplayOrderEntry);
            return;
        }
        var task = viewModel.FirstInvalidTask;
        if (task is null) return;
        var automationId = viewModel.FirstInvalidField switch
        {
            "ServiceId" => task.ServiceAutomationId,
            "TimeCategoryId" => task.TimeCategoryAutomationId,
            "WorkMinutes" => task.WorkMinutesAutomationId,
            "StartTime" => task.StartTimeAutomationId,
            "EndTime" => task.EndTimeAutomationId,
            _ => null,
        };
        if (automationId is null) return;
        await Task.Yield();
        var target = TaskList.GetVisualTreeDescendants().OfType<VisualElement>()
            .FirstOrDefault(element => element.AutomationId == automationId);
        await SettingsEditorFocus.FocusAsync(EditorScroll, target);
    }
}
