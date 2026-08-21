using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Home;

/// <summary>ホームから開く画面を、MAUI に依存しない形で抽象化します。</summary>
public interface IHomeNavigator
{
    Task OpenCalendarAsync(DateOnly selectedDate, CancellationToken cancellationToken);

    Task OpenCalculationDetailsAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken);

    Task OpenMonthlyAllowancesAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken);

    Task OpenUncalculatedDaysAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken);
}
