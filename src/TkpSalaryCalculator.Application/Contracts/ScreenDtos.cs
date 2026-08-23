using TkpSalaryCalculator.Domain.Models;

namespace TkpSalaryCalculator.Application.Contracts;

/// <summary>月間カレンダーと初期選択日の表示に必要なデータを保持します。</summary>
public sealed record CalendarMonthScreenDto(
    IReadOnlyList<CalendarDayDto> Days,
    DailySalaryDto SelectedDay);

/// <summary>日別一覧の給与行と基本シフト候補を同じ読取コンテキストで保持します。</summary>
public sealed record DayScreenDto(
    DailySalaryDto DailySalary,
    MonthSettingsDto Settings,
    BasicShiftPreviewDto BasicShiftPreview);

/// <summary>勤務入力画面で再利用する入力候補、編集対象および祝日データを保持します。</summary>
public sealed record WorkEditorScreenDto(
    WorkInputOptionsDto InputOptions,
    WorkRecordDto? ExistingRecord,
    HolidayCalendar HolidayCalendar);
