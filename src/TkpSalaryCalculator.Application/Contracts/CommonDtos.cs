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

/// <summary>プレゼンテーション層向けの正規化済み勤務記録を表します。</summary>
/// <param name="Id">記録識別子。</param>
/// <param name="WorkDate">勤務を開始した現地日付。</param>
/// <param name="ServiceId">選択されたサービス。</param>
/// <param name="TimeCategoryId">選択された時間区分。任意時間入力の場合は <see langword="null"/>。</param>
/// <param name="InputMode">入力モード。</param>
/// <param name="WorkMinutes">正規化済みの勤務時間。</param>
/// <param name="StartTime">開始時刻。存在しない場合があります。</param>
/// <param name="EndTime">正規化済みの終了時刻。存在しない場合があります。</param>
/// <param name="SourceServicePresetId">記録の作成に使用した入力補助プリセット。存在しない場合があります。</param>
/// <param name="SourceBasicShiftId">シフトから記録を反映した場合の元シフト識別子。</param>
/// <param name="SourceWorkRecordId">日単位の複製によって記録を作成した場合の元記録識別子。</param>
public sealed record WorkRecordDto(
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
    WorkRecordId? SourceWorkRecordId);

/// <summary>1 件の勤務記録を作成または更新するためのプレゼンテーション層からの入力を保持します。</summary>
/// <param name="Id">更新対象の既存識別子。新規作成の場合は <see langword="null"/>。</param>
/// <param name="WorkDate">勤務開始の現地日付。</param>
/// <param name="ServiceId">選択されたサービス。</param>
/// <param name="TimeCategoryId">選択された時間区分。存在しない場合があります。</param>
/// <param name="InputMode">選択された入力モード。</param>
/// <param name="WorkMinutes">時間入力モードで入力された勤務時間。それ以外の場合は <see langword="null"/>。</param>
/// <param name="StartTime">必要な場合に入力された開始時刻。</param>
/// <param name="EndTime">時刻範囲入力モードで入力された終了時刻。</param>
/// <param name="SourceServicePresetId">入力補助に使用したプリセット。存在しない場合があります。</param>
/// <param name="OperationId">新規保存の再試行と連続操作を一意に識別する値。更新では省略できます。</param>
public sealed record SaveWorkRecordCommand(
    WorkRecordId? Id,
    DateOnly WorkDate,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes? WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime,
    ServicePresetId? SourceServicePresetId,
    Guid? OperationId = null);

/// <summary>勤務入力用のサービスプリセット候補を表します。</summary>
/// <param name="Preset">現在の入力補助プリセット。</param>
/// <param name="IsAvailable">選択日の設定スナップショットで、変換せずにプリセットを使用できるかどうか。</param>
/// <param name="UsageCount">プリセットから作成され、保存された勤務記録の件数。</param>
/// <param name="IsMostRecentlyUsed">直近で確定した勤務記録に使用されたプリセットかどうか。</param>
/// <param name="Issues">利用できない候補を使用できない理由、または処理を妨げないその他の案内。</param>
public sealed record ServicePresetCandidateDto(
    ServicePresetDto Preset,
    bool IsAvailable,
    long UsageCount,
    bool IsMostRecentlyUsed,
    IReadOnlyList<IssueDto> Issues);

/// <summary>1 日分の勤務入力を開くために必要な設定と順序付け済み候補をすべて保持します。</summary>
/// <param name="WorkDate">選択された現地勤務日。</param>
/// <param name="Settings">入力と計算に使用する有効な月設定。</param>
/// <param name="PresetCandidates">アプリケーション層が利用可能な高頻度・直近使用プリセットを優先し、プレゼンテーション層での最終表示順に並べた候補。</param>
/// <param name="SuggestedValues">直近で確定した値を選択日に合わせて調整した、編集可能な初期候補。利用できない場合があります。</param>
public sealed record WorkInputOptionsDto(
    DateOnly WorkDate,
    MonthSettingsDto Settings,
    IReadOnlyList<ServicePresetCandidateDto> PresetCandidates,
    SaveWorkRecordCommand? SuggestedValues);

/// <summary>保存を伴わない検証、正規化、および給与プレビューを保持します。</summary>
/// <param name="NormalizedWorkMinutes">正規化に成功した場合の、算出または検証済み勤務時間。</param>
/// <param name="NormalizedStartTime">入力または適用対象の時間条件で必要となる、正規化済み開始時刻。</param>
/// <param name="NormalizedEndTime">必要な場合の正規化済み終了時刻。開始時刻より前の値は翌日を表します。</param>
/// <param name="Calculation">計算済みまたは未計算の給与結果。入力自体が無効な場合は <see langword="null"/>。</param>
/// <param name="CanSave">保存できるかどうか。入力が無効な場合は <see langword="false"/>。設定不足だけの場合は、未計算結果とともに <see langword="true"/> になる場合があります。</param>
/// <param name="Issues">処理を妨げる入力上の問題、または処理を妨げない設定不足警告と修正案内。</param>
public sealed record WorkRecordPreviewDto(
    WorkMinutes? NormalizedWorkMinutes,
    MinuteOfDay? NormalizedStartTime,
    MinuteOfDay? NormalizedEndTime,
    WorkSalaryCalculation? Calculation,
    bool CanSave,
    IReadOnlyList<IssueDto> Issues);

/// <summary>保存済み記録と、その時点での給与状態を保持します。</summary>
/// <param name="WorkRecord">正規化された保存済み記録。</param>
/// <param name="Calculation">決定論的な計算結果または設定不足の結果。</param>
/// <param name="Warnings">保存を妨げなかった警告。</param>
public sealed record SaveWorkRecordResultDto(
    WorkRecordDto WorkRecord,
    WorkSalaryCalculation Calculation,
    IReadOnlyList<IssueDto> Warnings);

/// <summary>保存済み勤務記録と、その計算根拠および結果を組み合わせます。</summary>
/// <param name="WorkRecord">保存済みの正規化された勤務内容。</param>
/// <param name="Calculation">計算内訳または明示的な設定不足結果。</param>
/// <param name="ServiceDisplayName">計算時の設定スナップショットに保存されたサービス表示名。</param>
/// <param name="TimeCategoryDisplayName">計算時の設定スナップショットに保存された時間区分表示名。</param>
/// <param name="SettingMonth">計算に使用した設定対象年月。</param>
public sealed record WorkRecordSalaryDto(
    WorkRecordDto WorkRecord,
    WorkSalaryCalculation Calculation,
    string? ServiceDisplayName = null,
    string? TimeCategoryDisplayName = null,
    YearMonth? SettingMonth = null);

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
