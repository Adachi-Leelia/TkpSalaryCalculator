using System.Globalization;
using System.ComponentModel;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Calendar;

/// <summary>SCR-WORK-01 の勤務入力・編集画面を表示します。</summary>
public partial class WorkEditorPage : ContentPage, IQueryAttributable
{
    public const string DateParameter = "date";
    public const string WorkRecordIdParameter = "workRecordId";
    private readonly WorkEditorViewModel viewModel;

    public WorkEditorPage(WorkEditorViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
        this.viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue(DateParameter, out var dateValue) ||
            !DateOnly.TryParseExact(dateValue?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return;
        WorkRecordId? id = null;
        if (query.TryGetValue(WorkRecordIdParameter, out var idValue) && Guid.TryParse(idValue?.ToString(), out var guid))
            id = new WorkRecordId(guid);
        viewModel.Initialize(date, id);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadIfNeededAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.CancelPendingOperations();
        base.OnDisappearing();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(WorkEditorViewModel.FirstInvalidField) ||
            string.IsNullOrWhiteSpace(viewModel.FirstInvalidField)) return;
        Dispatcher.Dispatch(() => _ = FocusFirstInvalidFieldAsync());
    }

    private async Task FocusFirstInvalidFieldAsync()
    {
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
        var target = TaskList.GetVisualTreeDescendants()
            .OfType<VisualElement>()
            .FirstOrDefault(element => element.AutomationId == automationId);
        if (target is null) return;
        if (!target.IsVisible) return;
        await EditorScroll.ScrollToAsync(target, ScrollToPosition.MakeVisible, true);
        target.Focus();
    }
}
