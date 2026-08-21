namespace TkpSalaryCalculator.App.Presentation.Features.Home;

/// <summary>SCR-HOME-01 の給与期間サマリーを表示します。</summary>
public partial class HomePage : ContentPage
{
    private readonly HomeViewModel viewModel;

    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.CancelPendingOperations();
        base.OnDisappearing();
    }
}
