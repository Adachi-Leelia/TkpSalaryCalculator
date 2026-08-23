using TkpSalaryCalculator.Domain.ValueObjects;
namespace TkpSalaryCalculator.App.Presentation.Features.Settings;
public partial class BasicShiftEditorPage : ContentPage, IQueryAttributable
{
    public const string IdParameter = "basicShiftId"; private readonly BasicShiftEditorViewModel viewModel;
    public BasicShiftEditorPage(BasicShiftEditorViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    public void ApplyQueryAttributes(IDictionary<string, object> query) => viewModel.Initialize(query.TryGetValue(IdParameter, out var value) && Guid.TryParse(value?.ToString(), out var id) ? new BasicShiftId(id) : null);
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.LoadAsync(); }
    protected override void OnDisappearing() { viewModel.CancelPendingOperations(); base.OnDisappearing(); }
}
