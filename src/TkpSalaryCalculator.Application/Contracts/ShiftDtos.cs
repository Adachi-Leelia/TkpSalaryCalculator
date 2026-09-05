using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Contracts;

/// <summary>基本シフト内の正規化済み勤務タスクを表します。</summary>
public sealed record BasicShiftTaskDto(
    BasicShiftTaskId Id,
    ServicePresetId? ServicePresetId,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime,
    DisplayOrder DisplayOrder);

/// <summary>1件以上の勤務タスクを持つ再利用可能な基本シフトを表します。</summary>
public sealed record BasicShiftDto(
    BasicShiftId Id,
    DayOfWeek Weekday,
    IReadOnlyList<BasicShiftTaskDto> Tasks,
    DisplayOrder DisplayOrder,
    bool IsEnabled)
{
    /// <summary>親情報と全タスクの値を構造的に比較します。</summary>
    public bool Equals(BasicShiftDto? other)
    {
        return other is not null &&
            Id == other.Id &&
            Weekday == other.Weekday &&
            DisplayOrder == other.DisplayOrder &&
            IsEnabled == other.IsEnabled &&
            Tasks.SequenceEqual(other.Tasks);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Weekday);
        hash.Add(DisplayOrder);
        hash.Add(IsEnabled);
        foreach (var task in Tasks)
        {
            hash.Add(task);
        }
        return hash.ToHashCode();
    }

}

/// <summary>基本シフト内の勤務タスクを作成または更新する入力を保持します。</summary>
public sealed record SaveBasicShiftTaskCommand(
    BasicShiftTaskId Id,
    ServicePresetId? ServicePresetId,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes? WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime,
    DisplayOrder DisplayOrder);

/// <summary>1件以上のタスクを持つ基本シフトを作成または置換する入力を保持します。</summary>
public sealed record SaveBasicShiftCommand(
    BasicShiftId? Id,
    DayOfWeek Weekday,
    IReadOnlyList<SaveBasicShiftTaskCommand> Tasks,
    DisplayOrder DisplayOrder,
    bool IsEnabled)
{
    /// <summary>親入力と全タスクの値を構造的に比較します。</summary>
    public bool Equals(SaveBasicShiftCommand? other)
    {
        return other is not null &&
            Id == other.Id &&
            Weekday == other.Weekday &&
            DisplayOrder == other.DisplayOrder &&
            IsEnabled == other.IsEnabled &&
            Tasks.SequenceEqual(other.Tasks);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Weekday);
        hash.Add(DisplayOrder);
        hash.Add(IsEnabled);
        foreach (var task in Tasks)
        {
            hash.Add(task);
        }
        return hash.ToHashCode();
    }

}

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
