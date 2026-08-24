using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

public enum ServiceSettingsEditorMode
{
    AddService,
    AddTimeCategory,
    EditMonthlySetting,
    EditInputCandidate,
}

/// <summary>設定メニューと各編集画面の遷移を MAUI から分離します。</summary>
public interface ISettingsNavigator
{
    Task OpenServicesAsync(CancellationToken cancellationToken);
    Task OpenServiceEditorAsync(ServiceSettingsEditorMode mode, Guid? id, CancellationToken cancellationToken);
    Task OpenPremiumsAsync(CancellationToken cancellationToken);
    Task OpenPremiumEditorAsync(Guid? premiumId, CancellationToken cancellationToken);
    Task OpenCountBonusesAsync(CancellationToken cancellationToken);
    Task OpenCountBonusEditorAsync(Guid? countBonusId, CancellationToken cancellationToken);
    Task OpenPayrollPeriodAsync(CancellationToken cancellationToken);
    Task OpenMonthlyAllowancesAsync(CancellationToken cancellationToken);
    Task OpenMonthlyAllowanceEditorAsync(PayrollPeriodKey payrollPeriodKey, Guid? allowanceId, CancellationToken cancellationToken);
    Task OpenBasicShiftsAsync(CancellationToken cancellationToken);
    Task OpenBasicShiftEditorAsync(Guid? basicShiftId, CancellationToken cancellationToken);
    Task OpenDataManagementAsync(CancellationToken cancellationToken);
    Task OpenAppInformationAsync(CancellationToken cancellationToken);
    Task GoBackAsync(string? successMessage, CancellationToken cancellationToken);
}
