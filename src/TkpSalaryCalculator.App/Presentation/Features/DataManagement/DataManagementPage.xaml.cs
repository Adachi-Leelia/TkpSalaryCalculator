namespace TkpSalaryCalculator.App.Presentation.Features.DataManagement;

public partial class DataManagementPage : ContentPage
{
    private readonly DataManagementViewModel viewModel;

    public DataManagementPage(DataManagementViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
    }

    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
}
