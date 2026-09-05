using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Contracts;

/// <summary>プレゼンテーション層向けの検証または業務ルール上の問題を表します。</summary>
/// <param name="Code">機械判読可能な安定したコード。</param>
/// <param name="Field">関連する入力項目。存在しない場合があります。</param>
/// <param name="Message">安全に表示できる利用者向けの日本語メッセージ。</param>
public sealed record IssueDto(string Code, string? Field, string Message);

/// <summary>任意の警告を含む完了済みコマンドを表します。</summary>
/// <param name="Warnings">プレゼンテーション層で表示する、処理を妨げない問題。</param>
public sealed record CommandResultDto(IReadOnlyList<IssueDto> Warnings);

/// <summary>プレゼンテーション層向けの正規化済み勤務タスクを表します。</summary>
public sealed record WorkTaskDto(
    WorkTaskId Id,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime,
    DisplayOrder DisplayOrder,
    ServicePresetId? SourceServicePresetId)
{
    /// <summary>由来情報を除いたDomainタスクへ明示的に変換します。</summary>
    public WorkTask ToDomain() =>
        new(Id, ServiceId, TimeCategoryId, InputMode, WorkMinutes, StartTime, EndTime, DisplayOrder);
}

/// <summary>1件の訪問として保存された、1件以上の勤務タスクを保持します。</summary>
public sealed record WorkRecordDto(
    WorkRecordId Id,
    DateOnly WorkDate,
    IReadOnlyList<WorkTaskDto> Tasks,
    BasicShiftId? SourceBasicShiftId,
    WorkRecordId? SourceWorkRecordId)
{
    /// <summary>旧1タスク契約を親子DTOへ変換する一時互換コンストラクターです。</summary>
    public WorkRecordDto(
        WorkRecordId Id,
        DateOnly WorkDate,
        ServiceId ServiceId,
        TimeCategoryId? TimeCategoryId,
        WorkInputMode InputMode,
        WorkMinutes WorkMinutes,
        MinuteOfDay? StartTime,
        MinuteOfDay? EndTime,
        ServicePresetId? SourceServicePresetId,
        BasicShiftId? SourceBasicShiftId,
        WorkRecordId? SourceWorkRecordId)
        : this(Id, WorkDate,
        [
            new WorkTaskDto(new WorkTaskId(Id.Value), ServiceId, TimeCategoryId, InputMode, WorkMinutes,
                StartTime, EndTime, new DisplayOrder(0), SourceServicePresetId),
        ], SourceBasicShiftId, SourceWorkRecordId)
    {
    }

    /// <summary>由来情報を除いたDomain訪問集約へ明示的に変換します。</summary>
    public WorkRecord ToDomain() => new(Id, WorkDate, Tasks.Select(static task => task.ToDomain()).ToArray());

    // タスク3まで旧1タスク呼出し元をコンパイル可能に保つ一時アダプター。
    public ServiceId ServiceId
    {
        get => Tasks[0].ServiceId;
        init => Tasks = ReplaceFirstTask(Tasks, task => task with { ServiceId = value });
    }
    public TimeCategoryId? TimeCategoryId
    {
        get => Tasks[0].TimeCategoryId;
        init => Tasks = ReplaceFirstTask(Tasks, task => task with { TimeCategoryId = value });
    }
    public WorkInputMode InputMode
    {
        get => Tasks[0].InputMode;
        init => Tasks = ReplaceFirstTask(Tasks, task => task with { InputMode = value });
    }
    public WorkMinutes WorkMinutes
    {
        get => Tasks[0].WorkMinutes;
        init => Tasks = ReplaceFirstTask(Tasks, task => task with { WorkMinutes = value });
    }
    public MinuteOfDay? StartTime
    {
        get => Tasks[0].StartTime;
        init => Tasks = ReplaceFirstTask(Tasks, task => task with { StartTime = value });
    }
    public MinuteOfDay? EndTime
    {
        get => Tasks[0].EndTime;
        init => Tasks = ReplaceFirstTask(Tasks, task => task with { EndTime = value });
    }
    public ServicePresetId? SourceServicePresetId
    {
        get => Tasks[0].SourceServicePresetId;
        init => Tasks = ReplaceFirstTask(Tasks, task => task with { SourceServicePresetId = value });
    }

    /// <summary>親情報と全タスクの値を構造的に比較します。</summary>
    public bool Equals(WorkRecordDto? other)
    {
        return other is not null &&
            Id == other.Id &&
            WorkDate == other.WorkDate &&
            SourceBasicShiftId == other.SourceBasicShiftId &&
            SourceWorkRecordId == other.SourceWorkRecordId &&
            Tasks.SequenceEqual(other.Tasks);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(WorkDate);
        hash.Add(SourceBasicShiftId);
        hash.Add(SourceWorkRecordId);
        foreach (var task in Tasks)
        {
            hash.Add(task);
        }
        return hash.ToHashCode();
    }

    private static IReadOnlyList<WorkTaskDto> ReplaceFirstTask(
        IReadOnlyList<WorkTaskDto> tasks,
        Func<WorkTaskDto, WorkTaskDto> replace)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(replace);
        if (tasks.Count == 0)
        {
            throw new ArgumentException("訪問には1件以上のタスクが必要です。", nameof(tasks));
        }

        var updated = tasks.ToArray();
        updated[0] = replace(updated[0]);
        return updated;
    }
}

/// <summary>1件の勤務タスクを作成または更新するための入力を保持します。</summary>
public sealed record SaveWorkTaskCommand(
    WorkTaskId Id,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes? WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime,
    DisplayOrder DisplayOrder,
    ServicePresetId? SourceServicePresetId);

/// <summary>1件以上のタスクを持つ訪問を作成または更新する入力を保持します。</summary>
public sealed record SaveWorkRecordCommand(
    WorkRecordId? Id,
    DateOnly WorkDate,
    IReadOnlyList<SaveWorkTaskCommand> Tasks,
    Guid? OperationId = null)
{
    /// <summary>親入力と全タスクの値を構造的に比較します。</summary>
    public bool Equals(SaveWorkRecordCommand? other)
    {
        return other is not null &&
            Id == other.Id &&
            WorkDate == other.WorkDate &&
            OperationId == other.OperationId &&
            Tasks.SequenceEqual(other.Tasks);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(WorkDate);
        hash.Add(OperationId);
        foreach (var task in Tasks)
        {
            hash.Add(task);
        }
        return hash.ToHashCode();
    }

}

/// <summary>勤務入力用のサービスプリセット候補を表します。</summary>
/// <param name="Preset">現在の入力補助プリセット。</param>
/// <param name="IsAvailable">選択日の設定スナップショットで、変換せずにプリセットを使用できるかどうか。</param>
/// <param name="Issues">利用できない候補を使用できない理由、または処理を妨げないその他の案内。</param>
public sealed record ServicePresetCandidateDto(
    ServicePresetDto Preset,
    bool IsAvailable,
    IReadOnlyList<IssueDto> Issues);

/// <summary>1 日分の勤務入力を開くために必要な設定と順序付け済み候補をすべて保持します。</summary>
/// <param name="WorkDate">選択された現地勤務日。</param>
/// <param name="Settings">入力と計算に使用する有効な月設定。</param>
/// <param name="PresetCandidates">利用者が設定した表示順で並べた入力候補。</param>
public sealed record WorkInputOptionsDto(
    DateOnly WorkDate,
    MonthSettingsDto Settings,
    IReadOnlyList<ServicePresetCandidateDto> PresetCandidates);

/// <summary>保存を伴わずに正規化した勤務タスクと、その入力上の問題を保持します。</summary>
public sealed record WorkTaskPreviewDto(
    WorkTaskId WorkTaskId,
    WorkMinutes? NormalizedWorkMinutes,
    MinuteOfDay? NormalizedStartTime,
    MinuteOfDay? NormalizedEndTime,
    bool CanSave,
    IReadOnlyList<IssueDto> Issues);

/// <summary>保存を伴わない訪問全体の検証、タスク別正規化、および給与プレビューを保持します。</summary>
public sealed record WorkRecordPreviewDto(
    IReadOnlyList<WorkTaskPreviewDto> Tasks,
    WorkSalaryCalculation? Calculation,
    bool CanSave,
    IReadOnlyList<IssueDto> Issues)
{
    /// <summary>旧1タスク画面向けの一時互換コンストラクターです。</summary>
    public WorkRecordPreviewDto(
        WorkMinutes? normalizedWorkMinutes,
        MinuteOfDay? normalizedStartTime,
        MinuteOfDay? normalizedEndTime,
        WorkSalaryCalculation? calculation,
        bool canSave,
        IReadOnlyList<IssueDto> issues)
        : this(
            [new WorkTaskPreviewDto(default, normalizedWorkMinutes, normalizedStartTime,
                normalizedEndTime, canSave, issues)],
            calculation,
            canSave,
            issues)
    {
    }

    // タスク5まで旧1タスク画面をコンパイル可能に保つ一時アダプター。
    public WorkMinutes? NormalizedWorkMinutes => Tasks.Count == 1 ? Tasks[0].NormalizedWorkMinutes : null;
    public MinuteOfDay? NormalizedStartTime => Tasks.Count == 1 ? Tasks[0].NormalizedStartTime : null;
    public MinuteOfDay? NormalizedEndTime => Tasks.Count == 1 ? Tasks[0].NormalizedEndTime : null;
}

/// <summary>保存済み記録と、その時点での給与状態を保持します。</summary>
/// <param name="WorkRecord">正規化された保存済み記録。</param>
/// <param name="Calculation">決定論的な計算結果または設定不足の結果。</param>
/// <param name="Warnings">保存を妨げなかった警告。</param>
public sealed record SaveWorkRecordResultDto(
    WorkRecordDto WorkRecord,
    WorkSalaryCalculation Calculation,
    IReadOnlyList<IssueDto> Warnings);

/// <summary>保存済み勤務タスクと、その計算根拠および表示名を組み合わせます。</summary>
/// <param name="WorkTask">保存済みの正規化された勤務タスク。</param>
/// <param name="Calculation">計算内訳または明示的な設定不足結果。</param>
/// <param name="ServiceDisplayName">計算時の設定スナップショットに保存されたサービス表示名。</param>
/// <param name="TimeCategoryDisplayName">計算時の設定スナップショットに保存された時間区分表示名。</param>
public sealed record WorkTaskSalaryDto(
    WorkTaskDto WorkTask,
    TaskSalaryCalculation Calculation,
    string? ServiceDisplayName,
    string? TimeCategoryDisplayName);

/// <summary>保存済み訪問と、そのタスク別計算根拠および結果を組み合わせます。</summary>
public sealed record WorkRecordSalaryDto(
    WorkRecordDto WorkRecord,
    WorkSalaryCalculation Calculation,
    string? ServiceDisplayName = null,
    string? TimeCategoryDisplayName = null,
    YearMonth? SettingMonth = null,
    IReadOnlyList<WorkTaskSalaryDto>? Tasks = null);

/// <summary>勤務記録 1 件の計算内訳画面に必要なデータだけを保持します。</summary>
/// <param name="Period">勤務日を含む、両端の日付を含む給与期間。</param>
/// <param name="Record">指定された勤務記録と、その計算根拠および結果。</param>
public sealed record WorkRecordCalculationDto(
    PayrollPeriod Period,
    WorkRecordSalaryDto Record);

/// <summary>1 日分の計算詳細を保持します。</summary>
/// <param name="Date">現地日付。</param>
/// <param name="Records">保存済み勤務内容と各計算結果の組み合わせ。</param>
/// <param name="BasePaySubtotal">計算済み記録の基本給小計。</param>
/// <param name="PremiumSubtotal">計算済み記録の割増小計。</param>
/// <param name="CountBonusSubtotal">計算済み記録の件数加算小計。</param>
/// <param name="CalculatedSubtotal">計算に成功した記録の小計。</param>
/// <param name="UncalculatedCount">計算設定が不足している記録の件数。</param>
public sealed record DailySalaryDto(
    DateOnly Date,
    IReadOnlyList<WorkRecordSalaryDto> Records,
    YenAmount BasePaySubtotal,
    YenAmount PremiumSubtotal,
    YenAmount CountBonusSubtotal,
    YenAmount CalculatedSubtotal,
    int UncalculatedCount);

/// <summary>複製プレビューで評価した重複候補と設定が、確定時にも不変であることを確認します。</summary>
/// <param name="SourceDate">プレビュー対象の複製元日。</param>
/// <param name="TargetDate">プレビュー対象の複製先日。</param>
/// <param name="ExpectedTargetExistingWorkRecordCount">プレビュー時点の複製先にある勤務記録数。</param>
/// <param name="ExpectedEffectiveSnapshotId">プレビュー時点で複製先年月に有効だった設定スナップショット。</param>
/// <param name="ExpectedHolidayCalendarVersionId">複製先年月を初めて使用する際に適用する祝日データ版。</param>
public sealed record CopyDayConfirmationToken(
    DateOnly SourceDate,
    DateOnly TargetDate,
    int ExpectedTargetExistingWorkRecordCount,
    SettingSnapshotId ExpectedEffectiveSnapshotId,
    HolidayCalendarVersionId ExpectedHolidayCalendarVersionId);

/// <summary>1 日分の勤務記録を複製するための未保存の確認データを保持します。</summary>
/// <param name="SourceDate">複製元の現地日付。</param>
/// <param name="TargetDate">複製先の現地日付。</param>
/// <param name="SourceWorkRecordCount">複製される記録の件数。</param>
/// <param name="TargetExistingWorkRecordCount">複製先の日付に保存済みの記録件数。</param>
/// <param name="SourceSettingMonth">複製元記録が使用する暦上の設定月。</param>
/// <param name="TargetSettingMonth">複製された記録が使用する暦上の設定月。</param>
/// <param name="UsesDifferentSettingMonth">複製によって別の月のスナップショットで再計算されるかどうか。</param>
/// <param name="Issues">プレゼンテーション層向けの、処理を妨げる問題または重複警告。</param>
/// <param name="ConfirmationToken">確認時に評価した複製先設定を確定時に検証するトークン。</param>
public sealed record CopyDayPreviewDto(
    DateOnly SourceDate,
    DateOnly TargetDate,
    int SourceWorkRecordCount,
    int TargetExistingWorkRecordCount,
    YearMonth SourceSettingMonth,
    YearMonth TargetSettingMonth,
    bool UsesDifferentSettingMonth,
    IReadOnlyList<IssueDto> Issues,
    CopyDayConfirmationToken ConfirmationToken);

/// <summary>給与期間の概要に含まれる 1 件の手当行を保持します。</summary>
/// <param name="Id">手当識別子。</param>
/// <param name="DisplayName">表示名。</param>
/// <param name="Amount">円単位の金額。</param>
public sealed record MonthlyAllowanceDto(MonthlyAllowanceId Id, string DisplayName, YenAmount Amount);

/// <summary>月額手当画面に必要な給与期間境界と手当だけを保持します。</summary>
/// <param name="Period">両端の日付を含む給与期間。</param>
/// <param name="Allowances">期間に 1 回適用される手当。</param>
public sealed record MonthlyAllowancePeriodDto(
    PayrollPeriod Period,
    IReadOnlyList<MonthlyAllowanceDto> Allowances);

/// <summary>プレゼンテーション層向けの完全な給与期間読み取りモデルを保持します。</summary>
/// <param name="Period">両端の日付を含む給与期間。</param>
/// <param name="Days">期間内の計算済み日別結果。</param>
/// <param name="Allowances">期間に 1 回適用される手当。</param>
/// <param name="BasePaySubtotal">計算済み記録の基本給小計。</param>
/// <param name="PremiumSubtotal">計算済み記録の割増小計。</param>
/// <param name="CountBonusSubtotal">計算済み記録の件数加算小計。</param>
/// <param name="AllowanceSubtotal">給与期間に直接適用される手当の小計。</param>
/// <param name="CalculatedSubtotal">計算済み記録と手当の合計。</param>
/// <param name="UncalculatedCount">未計算の勤務記録数。</param>
public sealed record PayrollPeriodSummaryDto(
    PayrollPeriod Period,
    IReadOnlyList<DailySalaryDto> Days,
    IReadOnlyList<MonthlyAllowanceDto> Allowances,
    YenAmount BasePaySubtotal,
    YenAmount PremiumSubtotal,
    YenAmount CountBonusSubtotal,
    YenAmount AllowanceSubtotal,
    YenAmount CalculatedSubtotal,
    int UncalculatedCount);

/// <summary>ホーム画面向けの年間給与見込み累計を保持します。</summary>
/// <param name="PeriodStart">年間区分の開始給与期間。</param>
/// <param name="PeriodEnd">年間区分の終了給与期間。</param>
/// <param name="AccumulationEnd">実際に累計した最後の給与期間。</param>
/// <param name="CalculatedSubtotal">年間給与見込み累計。</param>
/// <param name="UncalculatedCount">年間範囲内の未計算勤務記録数。</param>
public sealed record AnnualSalarySummaryDto(
    PayrollPeriodKey PeriodStart,
    PayrollPeriodKey PeriodEnd,
    PayrollPeriodKey AccumulationEnd,
    YenAmount CalculatedSubtotal,
    int UncalculatedCount);

/// <summary>同じ一括読取から生成したホーム画面の月次・年間サマリーを保持します。</summary>
/// <param name="MonthlySummary">選択中の給与期間サマリー。</param>
/// <param name="AnnualSummary">選択中の給与期間までの年間サマリー。</param>
public sealed record HomeSalarySummaryDto(
    PayrollPeriodSummaryDto MonthlySummary,
    AnnualSalarySummaryDto AnnualSummary);

/// <summary>暦日 1 日分の読み取りモデルを保持します。</summary>
/// <param name="Date">現地日付。</param>
/// <param name="WorkRecordCount">保存済み記録の件数。</param>
/// <param name="CalculatedSubtotal">計算済み小計。</param>
/// <param name="UncalculatedCount">未計算記録の件数。</param>
/// <param name="BasicShiftCandidateCount">未反映のシフト候補数。</param>
public sealed record CalendarDayDto(
    DateOnly Date,
    int WorkRecordCount,
    YenAmount CalculatedSubtotal,
    int UncalculatedCount,
    int BasicShiftCandidateCount);
