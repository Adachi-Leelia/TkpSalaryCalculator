using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Calendar;

/// <summary>カレンダー、日別一覧、勤務編集間の画面遷移を抽象化します。</summary>
public interface ICalendarNavigator
{
    Task OpenDayAsync(DateOnly date, CancellationToken cancellationToken);

    Task OpenWorkEditorAsync(DateOnly date, WorkRecordId? workRecordId, CancellationToken cancellationToken);

    Task OpenCalculationDetailsAsync(DateOnly date, WorkRecordId workRecordId, CancellationToken cancellationToken);

    Task GoBackAsync(string? successMessage, CancellationToken cancellationToken);
}
