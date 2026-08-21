namespace TkpSalaryCalculator.App.Presentation.Features.Setup;

public partial class InitialSetupFlowPage : ContentPage
{
    private readonly InitialSetupFlowViewModel viewModel;

    public InitialSetupFlowPage(InitialSetupFlowViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
        viewModel.ErrorFocusRequested += OnErrorFocusRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.CancelPendingOperations();
        base.OnDisappearing();
    }

    private void OnErrorFocusRequested(object? sender, string fieldId)
    {
        Dispatcher.Dispatch(async () =>
        {
            await Task.Yield();
            var target = FindByAutomationId(SetupScrollView, fieldId)
                ?? FindByAutomationId(SetupScrollView, "Setup.ErrorSummary");
            if (target is null) return;
            await SetupScrollView.ScrollToAsync(target, ScrollToPosition.Center, true);
            target.Focus();
        });
    }

    private static VisualElement? FindByAutomationId(IVisualTreeElement parent, string automationId)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is VisualElement element && element.AutomationId == automationId) return element;
            if (child is IVisualTreeElement visualChild && FindByAutomationId(visualChild, automationId) is { } descendant)
                return descendant;
        }
        return null;
    }
}
