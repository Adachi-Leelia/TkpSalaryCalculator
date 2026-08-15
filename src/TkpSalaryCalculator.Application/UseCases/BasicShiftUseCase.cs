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
        ApplicationSupport.ValidateId(command.ServiceId.Value, nameof(command.ServiceId));
        if (command.TimeCategoryId is { } categoryId) ApplicationSupport.ValidateId(categoryId.Value, nameof(command.TimeCategoryId));
        if (!Enum.IsDefined(command.Weekday)) throw new ApplicationErrorException("SHIFT_WEEKDAY_INVALID", "曜日を選び直してください。", "Weekday");
        var (Minutes, Start, End, Issues) = ApplicationSupport.Normalize(command.InputMode, command.WorkMinutes, command.StartTime, command.EndTime, false);
        if (Issues.Count != 0 || Minutes is null)
            throw new ApplicationErrorException(Issues[0].Code, Issues[0].Message, Issues[0].Field);
        var dto = new BasicShiftDto(command.Id ?? new BasicShiftId(Guid.NewGuid()), command.Weekday,
            command.ServicePresetId, command.ServiceId, command.TimeCategoryId, command.InputMode,
            Minutes.Value, Start, End, command.DisplayOrder, command.IsEnabled);
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
                    throw new ApplicationErrorException(candidate.Issues.FirstOrDefault()?.Code ?? "SHIFT_CANNOT_APPLY",
                        candidate.Issues.FirstOrDefault()?.Message ?? "選択した基本シフトは反映できません。");
                selected.Add(candidate.Shift);
            }
            var results = new List<SaveWorkRecordResultDto>(selected.Count);
            foreach (var shift in selected)
            {
                var work = new WorkRecordDto(new WorkRecordId(Guid.NewGuid()), command.WorkDate, shift.ServiceId,
                    shift.TimeCategoryId, shift.InputMode, shift.WorkMinutes, shift.StartTime, shift.EndTime,
                    shift.ServicePresetId, shift.Id, null);
                var calculation = calculator.Calculate(new WorkSalaryCalculationRequest(ApplicationSupport.ToDomain(work),
                    ApplicationSupport.ForCalculationDate(snapshot, command.WorkDate, calendar), calendar));
                await records.UpsertAsync(work, token).ConfigureAwait(false);
                results.Add(new(work, calculation, ApplicationSupport.CalculationIssues(calculation)));
            }
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
            return (IReadOnlyList<SaveWorkRecordResultDto>)results;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static BasicShiftPreviewDto BuildPreview(DateOnly workDate, IReadOnlyList<BasicShiftDto> source,
        IReadOnlyList<WorkRecordDto> existing, SettingSnapshot snapshot, HolidayCalendar calendar,
        ISalaryCalculator calculator)
    {
        var candidates = source.OrderBy(x => x.DisplayOrder.Value).ThenBy(x => x.Id.Value).Select(shift =>
        {
            var issues = new List<IssueDto>();
            var already = existing.Any(x => x.SourceBasicShiftId == shift.Id);
            if (!shift.IsEnabled) issues.Add(ApplicationSupport.Issue("SHIFT_DISABLED", "この基本シフトは無効になっています。"));
            issues.AddRange(ApplicationSupport.ValidateSelection(snapshot, shift.ServiceId, shift.TimeCategoryId));
            if (ApplicationSupport.RequiresStartTime(snapshot, shift.ServiceId, workDate, calendar) && shift.StartTime is null)
                issues.Add(ApplicationSupport.Issue("SHIFT_START_REQUIRED_FOR_PREMIUM", "時刻条件付き割増を計算するため、基本シフトに開始時刻を設定してください。"));
            if (already) issues.Add(ApplicationSupport.Issue("SHIFT_ALREADY_APPLIED", "この基本シフトは選択した日へ既に反映されています。"));
            var similar = existing.Any(x => x.SourceBasicShiftId is null && x.ServiceId == shift.ServiceId &&
                x.TimeCategoryId == shift.TimeCategoryId && x.WorkMinutes == shift.WorkMinutes && x.StartTime == shift.StartTime);
            if (similar) issues.Add(ApplicationSupport.Issue("SHIFT_SIMILAR_MANUAL_RECORD", "似た内容の手入力勤務があります。重複しないか確認してください。"));
            var blocking = !shift.IsEnabled || already || issues.Any(x => x.Code is "WORK_SERVICE_UNAVAILABLE" or "WORK_TIME_CATEGORY_UNAVAILABLE" or "SHIFT_START_REQUIRED_FOR_PREMIUM");
            if (!blocking)
            {
                var candidate = new WorkRecordDto(new WorkRecordId(Guid.NewGuid()), workDate, shift.ServiceId,
                    shift.TimeCategoryId, shift.InputMode, shift.WorkMinutes, shift.StartTime, shift.EndTime,
                    shift.ServicePresetId, shift.Id, null);
                var calculation = calculator.Calculate(new WorkSalaryCalculationRequest(ApplicationSupport.ToDomain(candidate),
                    ApplicationSupport.ForCalculationDate(snapshot, workDate, calendar), calendar));
                if (calculation.Status == SalaryCalculationStatus.Uncalculated)
                {
                    issues.Add(ApplicationSupport.Issue("SHIFT_CALCULATION_SETTINGS_REQUIRED", "給与計算に必要な単価設定が不足しているため、この基本シフトは反映できません。"));
                    blocking = true;
                }
            }
            return new BasicShiftCandidateDto(shift, !blocking, already, similar, issues);
        }).ToArray();
        return new(workDate, candidates, existing.Count);
    }
}
