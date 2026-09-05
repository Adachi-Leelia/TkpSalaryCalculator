using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Internal;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.UseCases;

/// <summary>曜日別基本シフトの管理、反映前確認、および重複を防止した反映を実装します。</summary>
/// <remarks>必要なポートとドメインサービスを指定して生成します。</remarks>
public sealed class BasicShiftUseCase(IBasicShiftRepository shifts, IWorkRecordRepository records,
    ISettingSnapshotRepository settings, IHolidayCalendarRepository holidays, ISalaryCalculator calculator,
    ITransactionRunner transactions, IAppMetadataRepository metadata, IUtcClock clock) : IBasicShiftUseCase
{
    private readonly IBasicShiftRepository shifts = shifts ?? throw new ArgumentNullException(nameof(shifts));
    private readonly IWorkRecordRepository records = records ?? throw new ArgumentNullException(nameof(records));
    private readonly ISettingSnapshotRepository settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IHolidayCalendarRepository holidays = holidays ?? throw new ArgumentNullException(nameof(holidays));
    private readonly ISalaryCalculator calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
    private readonly ITransactionRunner transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    private readonly IAppMetadataRepository metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));


    /// <inheritdoc />
    public async Task<IReadOnlyList<BasicShiftDto>> GetForWeekdayAsync(DayOfWeek weekday, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(weekday)) throw new ArgumentOutOfRangeException(nameof(weekday));
        cancellationToken.ThrowIfCancellationRequested();
        return [.. (await shifts.GetForWeekdayAsync(weekday, cancellationToken).ConfigureAwait(false))
            .OrderBy(x => x.DisplayOrder.Value).ThenBy(x => x.Id.Value)];
    }

    /// <inheritdoc />
    public async Task<BasicShiftDto> SaveAsync(SaveBasicShiftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.Id is { } id) ApplicationSupport.ValidateId(id.Value, nameof(command.Id));
        if (!Enum.IsDefined(command.Weekday)) throw new ApplicationErrorException("SHIFT_WEEKDAY_INVALID", "曜日を選び直してください。", "Weekday");
        if (command.Tasks is null || command.Tasks.Count == 0)
            throw new ApplicationErrorException("SHIFT_TASK_REQUIRED", "基本シフトには1件以上のタスクを登録してください。", "Tasks");
        if (command.Tasks.Any(task => task is null || task.Id.Value == Guid.Empty) ||
            command.Tasks.Select(task => task.Id).Distinct().Count() != command.Tasks.Count)
            throw new ApplicationErrorException("SHIFT_TASK_ID_INVALID", "タスクの識別子が不正または重複しています。", "Tasks");
        if (command.Tasks.Select(task => task.DisplayOrder).Distinct().Count() != command.Tasks.Count)
            throw new ApplicationErrorException("SHIFT_TASK_ORDER_INVALID", "タスクの表示順が重複しています。", "Tasks");
        var tasks = command.Tasks.OrderBy(task => task.DisplayOrder.Value).Select((task, index) =>
        {
            var field = $"Tasks[{task.Id.Value:D}]";
            if (task.ServiceId.Value == Guid.Empty)
                throw new ApplicationErrorException("SHIFT_SERVICE_REQUIRED", "サービスを選択してください。", $"{field}.ServiceId");
            if (task.TimeCategoryId is { } categoryId) ApplicationSupport.ValidateId(categoryId.Value, $"{field}.TimeCategoryId");
            if (task.ServicePresetId is { } presetId) ApplicationSupport.ValidateId(presetId.Value, $"{field}.ServicePresetId");
            var (minutes, start, end, issues) = ApplicationSupport.Normalize(task.InputMode, task.WorkMinutes, task.StartTime, task.EndTime, false);
            if (issues.Count != 0 || minutes is null)
                throw new ApplicationErrorException(issues[0].Code, issues[0].Message, $"{field}.{issues[0].Field}");
            return new BasicShiftTaskDto(task.Id, task.ServicePresetId, task.ServiceId, task.TimeCategoryId,
                task.InputMode, minutes.Value, start, end, new DisplayOrder(index));
        }).ToArray();
        var dto = new BasicShiftDto(command.Id ?? new BasicShiftId(Guid.NewGuid()), command.Weekday,
            tasks, command.DisplayOrder, command.IsEnabled);
        await transactions.ExecuteAsync(async token =>
        {
            await shifts.UpsertAsync(dto, token).ConfigureAwait(false);
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return dto;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(BasicShiftId id, CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidateId(id.Value, nameof(id));
        cancellationToken.ThrowIfCancellationRequested();
        await transactions.ExecuteAsync(async token =>
        {
            if (await shifts.FindAsync(id, token).ConfigureAwait(false) is null)
                throw new ApplicationErrorException("SHIFT_NOT_FOUND", "削除する基本シフトが見つかりませんでした。");
            await shifts.DeleteAsync(id, token).ConfigureAwait(false);
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BasicShiftPreviewDto> PreviewForDateAsync(DateOnly workDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await settings.GetEffectiveForMonthAsync(ApplicationSupport.ToYearMonth(workDate), cancellationToken).ConfigureAwait(false);
        var source = await GetForWeekdayAsync(workDate.DayOfWeek, cancellationToken).ConfigureAwait(false);
        var calendar = await holidays.GetAsync(snapshot.HolidayCalendarVersionId, cancellationToken).ConfigureAwait(false);
        var existing = new List<WorkRecordDto>();
        await foreach (var item in records.StreamRangeAsync(workDate, workDate, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false)) existing.Add(item);
        return BuildPreview(workDate, source, existing, snapshot, calendar, calculator);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SaveWorkRecordResultDto>> ApplyAsync(ApplyBasicShiftsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.BasicShiftIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.BasicShiftIds.Count == 0) throw new ApplicationErrorException("SHIFT_SELECTION_REQUIRED", "反映する基本シフトを選択してください。", "BasicShiftIds");
        foreach (var id in command.BasicShiftIds) ApplicationSupport.ValidateId(id.Value, nameof(command.BasicShiftIds));
        if (command.BasicShiftIds.Distinct().Count() != command.BasicShiftIds.Count)
            throw new ApplicationErrorException("SHIFT_SELECTION_DUPLICATED", "同じ基本シフトが複数回選択されています。", "BasicShiftIds");
        return await transactions.ExecuteAsync(async token =>
        {
            var snapshot = await settings.EnsureForMonthAsync(ApplicationSupport.ToYearMonth(command.WorkDate), token).ConfigureAwait(false);
            var all = await shifts.GetForWeekdayAsync(command.WorkDate.DayOfWeek, token).ConfigureAwait(false);
            var existing = new List<WorkRecordDto>();
            await foreach (var item in records.StreamRangeAsync(command.WorkDate, command.WorkDate, token).WithCancellation(token).ConfigureAwait(false)) existing.Add(item);
            var calendar = await holidays.GetAsync(snapshot.HolidayCalendarVersionId, token).ConfigureAwait(false);
            var preview = BuildPreview(command.WorkDate, all, existing, snapshot, calendar, calculator);
            var selected = new List<BasicShiftDto>();
            foreach (var id in command.BasicShiftIds)
            {
                var candidate = preview.Candidates.FirstOrDefault(x => x.Shift.Id == id)
                    ?? throw new ApplicationErrorException("SHIFT_NOT_FOUND", "選択した基本シフトが見つかりませんでした。");
                if (!candidate.CanApply)
                {
                    var issue = candidate.Issues.FirstOrDefault(issue => issue.Code != "SHIFT_SIMILAR_MANUAL_RECORD");
                    throw new ApplicationErrorException(issue?.Code ?? "SHIFT_CANNOT_APPLY",
                        issue?.Message ?? "選択した基本シフトは反映できません。", issue?.Field);
                }
                selected.Add(candidate.Shift);
            }
            var results = new List<SaveWorkRecordResultDto>(selected.Count);
            var calculationSnapshot = ApplicationSupport.ForCalculationDate(snapshot, command.WorkDate, calendar);
            foreach (var shift in selected)
            {
                var work = CreateVisit(shift, command.WorkDate);
                var calculation = calculator.Calculate(new WorkSalaryCalculationRequest(ApplicationSupport.ToDomain(work),
                    calculationSnapshot, calendar));
                await records.UpsertAsync(work, token).ConfigureAwait(false);
                results.Add(new(work, calculation, ApplicationSupport.CalculationIssues(calculation)));
            }
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
            return (IReadOnlyList<SaveWorkRecordResultDto>)results;
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static BasicShiftPreviewDto BuildPreview(DateOnly workDate, IReadOnlyList<BasicShiftDto> source,
        IReadOnlyList<WorkRecordDto> existing, SettingSnapshot snapshot, HolidayCalendar calendar,
        ISalaryCalculator calculator)
    {
        var calculationSnapshot = ApplicationSupport.ForCalculationDate(snapshot, workDate, calendar);
        var candidates = source.OrderBy(x => x.DisplayOrder.Value).ThenBy(x => x.Id.Value).Select(shift =>
        {
            var issues = new List<IssueDto>();
            var already = existing.Any(x => x.SourceBasicShiftId == shift.Id);
            if (!shift.IsEnabled) issues.Add(ApplicationSupport.Issue("SHIFT_DISABLED", "この基本シフトは無効になっています。"));
            foreach (var task in shift.Tasks.OrderBy(task => task.DisplayOrder.Value))
            {
                var field = $"Tasks[{task.Id.Value:D}]";
                issues.AddRange(ApplicationSupport.ValidateSelection(snapshot, task.ServiceId, task.TimeCategoryId)
                    .Select(issue => issue with { Field = $"{field}.{issue.Field}", Message = $"タスク {task.DisplayOrder.Value + 1}: {issue.Message}" }));
                if (ApplicationSupport.RequiresStartTime(snapshot, task.ServiceId, workDate, calendar) && task.StartTime is null)
                    issues.Add(ApplicationSupport.Issue("SHIFT_START_REQUIRED_FOR_PREMIUM",
                        $"タスク {task.DisplayOrder.Value + 1}: 時刻条件付き割増を計算するため、基本シフトに開始時刻を設定してください。", $"{field}.StartTime"));
            }
            if (already) issues.Add(ApplicationSupport.Issue("SHIFT_ALREADY_APPLIED", "この基本シフトは選択した日へ既に反映されています。"));
            var taskCounts = shift.Tasks.GroupBy(task => new TaskContent(task.ServiceId, task.TimeCategoryId,
                task.InputMode, task.WorkMinutes, task.StartTime, task.EndTime)).ToDictionary(group => group.Key, group => group.Count());
            var similar = existing.Any(work => work.SourceBasicShiftId is null && work.Tasks.Count == shift.Tasks.Count &&
                work.Tasks.GroupBy(task => new TaskContent(task.ServiceId, task.TimeCategoryId, task.InputMode,
                    task.WorkMinutes, task.StartTime, task.EndTime))
                    .All(group => taskCounts.GetValueOrDefault(group.Key) == group.Count()));
            if (similar) issues.Add(ApplicationSupport.Issue("SHIFT_SIMILAR_MANUAL_RECORD", "似た内容の手入力勤務があります。重複しないか確認してください。"));
            var blocking = !shift.IsEnabled || already || issues.Any(x => x.Code is "WORK_SERVICE_UNAVAILABLE" or "WORK_TIME_CATEGORY_UNAVAILABLE" or "SHIFT_START_REQUIRED_FOR_PREMIUM");
            if (!blocking)
            {
                var candidate = CreateVisit(shift, workDate, preserveTaskIds: true);
                var calculation = calculator.Calculate(new WorkSalaryCalculationRequest(ApplicationSupport.ToDomain(candidate),
                    calculationSnapshot, calendar));
                if (calculation.Status == SalaryCalculationStatus.Uncalculated)
                {
                    foreach (var issue in ApplicationSupport.CalculationIssues(calculation))
                    {
                        var task = shift.Tasks.FirstOrDefault(task => issue.Field?.StartsWith($"Tasks[{task.Id.Value:D}]", StringComparison.Ordinal) == true);
                        issues.Add(issue with { Code = "SHIFT_CALCULATION_SETTINGS_REQUIRED",
                            Message = $"タスク {task?.DisplayOrder.Value + 1}: {issue.Message}" });
                    }
                    blocking = true;
                }
            }
            return new BasicShiftCandidateDto(shift, !blocking, already, similar, issues);
        }).ToArray();
        return new(workDate, candidates, existing.Count);
    }

    private static WorkRecordDto CreateVisit(BasicShiftDto shift, DateOnly workDate, bool preserveTaskIds = false) =>
        new(new WorkRecordId(Guid.NewGuid()), workDate,
            shift.Tasks.OrderBy(task => task.DisplayOrder.Value).Select(task => new WorkTaskDto(
                new WorkTaskId(preserveTaskIds ? task.Id.Value : Guid.NewGuid()), task.ServiceId, task.TimeCategoryId,
                task.InputMode, task.WorkMinutes, task.StartTime, task.EndTime, task.DisplayOrder, task.ServicePresetId)).ToArray(),
            shift.Id, null);

    private sealed record TaskContent(ServiceId ServiceId, TimeCategoryId? TimeCategoryId, WorkInputMode InputMode,
        WorkMinutes WorkMinutes, MinuteOfDay? StartTime, MinuteOfDay? EndTime);
}
