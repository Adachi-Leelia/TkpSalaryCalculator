namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

/// <summary>設定メニューと各編集画面の遷移を MAUI から分離します。</summary>
public interface ISettingsNavigator
{
    Task OpenServicesAsync(CancellationToken cancellationToken);
    Task OpenServiceEditorAsync(Guid? serviceId, CancellationToken cancellationToken);
    Task OpenPremiumsAsync(CancellationToken cancellationToken);
    Task OpenPremiumEditorAsync(Guid? premiumId, CancellationToken cancellationToken);
    Task OpenCountBonusesAsync(CancellationToken cancellationToken);
    Task OpenCountBonusEditorAsync(Guid? countBonusId, CancellationToken cancellationToken);
    Task OpenPayrollPeriodAsync(CancellationToken cancellationToken);
    Task GoBackAsync(string? successMessage, CancellationToken cancellationToken);
}
