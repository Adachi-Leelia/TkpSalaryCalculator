using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Contracts;

/// <summary>再利用可能な基本シフトを表します。</summary>
/// <param name="Id">シフト識別子。</param>
/// <param name="Weekday">対象の曜日。</param>
/// <param name="ServicePresetId">元のプリセット。存在しない場合があります。</param>
/// <param name="ServiceId">勤務記録へコピーする具体的なサービス。</param>
/// <param name="TimeCategoryId">具体的な時間区分。存在しない場合があります。</param>
/// <param name="InputMode">入力モード。</param>
/// <param name="WorkMinutes">正規化済みの勤務時間。</param>
/// <param name="StartTime">開始時刻。存在しない場合があります。</param>
/// <param name="EndTime">終了時刻。存在しない場合があります。</param>
/// <param name="DisplayOrder">曜日内での表示順。</param>
/// <param name="IsEnabled">新規反映に使用できるシフトかどうか。</param>
public sealed record BasicShiftDto(
    BasicShiftId Id,
    DayOfWeek Weekday,
    ServicePresetId? ServicePresetId,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime,
    DisplayOrder DisplayOrder,
    bool IsEnabled);

/// <summary>基本シフトを作成または置換するための入力内容を保持します。</summary>
/// <param name="Id">既存の識別子。新規シフトの場合は <see langword="null"/>。</param>
/// <param name="Weekday">対象の曜日。</param>
/// <param name="ServicePresetId">元のプリセット。存在しない場合があります。</param>
/// <param name="ServiceId">具体的なサービス。</param>
/// <param name="TimeCategoryId">具体的な時間区分。存在しない場合があります。</param>
/// <param name="InputMode">入力モード。</param>
/// <param name="WorkMinutes"><see cref="WorkInputMode.Duration"/> の場合に入力された勤務時間。<see cref="WorkInputMode.TimeRange"/> の場合は <see langword="null"/> となり、アプリケーション層が開始時刻と終了時刻から算出します。</param>
/// <param name="StartTime">開始時刻。存在しない場合があります。</param>
/// <param name="EndTime">終了時刻。存在しない場合があります。</param>
/// <param name="DisplayOrder">曜日内での表示順。</param>
/// <param name="IsEnabled">反映に使用できるシフトかどうか。</param>
public sealed record SaveBasicShiftCommand(
    BasicShiftId? Id,
    DayOfWeek Weekday,
    ServicePresetId? ServicePresetId,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes? WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime,
    DisplayOrder DisplayOrder,
    bool IsEnabled);

/// <summary>1 件のシフト候補と、反映できない場合の理由を表します。</summary>
/// <param name="Shift">元のシフト。</param>
/// <param name="CanApply">選択した日付に対して有効かどうか。</param>
/// <param name="IsAlreadyApplied">このシフトが対象日へ反映済みかどうか。</param>
/// <param name="HasSimilarManualRecord">重複の可能性がある手動入力記録が存在するかどうか。</param>
/// <param name="Issues">処理を妨げる問題または警告。</param>
public sealed record BasicShiftCandidateDto(
    BasicShiftDto Shift,
    bool CanApply,
    bool IsAlreadyApplied,
    bool HasSimilarManualRecord,
    IReadOnlyList<IssueDto> Issues);

/// <summary>1 日分の、未保存のシフト反映プレビューを保持します。</summary>
/// <param name="WorkDate">選択した日付。</param>
/// <param name="Candidates">表示順に並んだ候補。</param>
/// <param name="ExistingWorkRecordCount">対象日に現在存在する勤務記録の件数。</param>
public sealed record BasicShiftPreviewDto(
    DateOnly WorkDate,
    IReadOnlyList<BasicShiftCandidateDto> Candidates,
    int ExistingWorkRecordCount);

/// <summary>選択したシフト候補を独立した勤務記録として確定します。</summary>
/// <param name="WorkDate">選択した日付。</param>
/// <param name="BasicShiftIds">選択した元シフト。</param>
public sealed record ApplyBasicShiftsCommand(DateOnly WorkDate, IReadOnlyList<BasicShiftId> BasicShiftIds);
