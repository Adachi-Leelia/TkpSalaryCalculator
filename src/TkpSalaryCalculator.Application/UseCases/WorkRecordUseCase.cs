using System.Collections.Concurrent;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Internal;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.UseCases;

/// <summary>勤務入力の検証、正規化、給与プレビュー、および原子的な保存を実装します。</summary>
public sealed class WorkRecordUseCase : IWorkRecordUseCase
{
    private readonly IWorkRecordRepository records;
    private readonly ISettingSnapshotRepository settings;
    private readonly IServicePresetRepository presets;
    private readonly IHolidayCalendarRepository holidays;
    private readonly ISalaryCalculator calculator;
    private readonly ITransactionRunner transactions;
    private readonly IAppMetadataRepository metadata;
    private readonly IUtcClock clock;
    private readonly ConcurrentDictionary<Guid, PendingSave> pendingSaves = new();

    /// <summary>必要なポートとドメインサービスを指定して生成します。</summary>
    public WorkRecordUseCase(
        IWorkRecordRepository records,
        ISettingSnapshotRepository settings,
        IServicePresetRepository presets,
        IHolidayCalendarRepository holidays,
        ISalaryCalculator calculator,
        ITransactionRunner transactions,
        IAppMetadataRepository metadata,
        IUtcClock clock)
    {
        this.records = records ?? throw new ArgumentNullException(nameof(records));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.presets = presets ?? throw new ArgumentNullException(nameof(presets));
        this.holidays = holidays ?? throw new ArgumentNullException(nameof(holidays));
        this.calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        this.transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<WorkInputOptionsDto> GetInputOptionsAsync(DateOnly workDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var month = ApplicationSupport.ToYearMonth(workDate);
        var snapshot = await settings.GetEffectiveForMonthAsync(month, cancellationToken).ConfigureAwait(false);
        var allPresets = await presets.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var usage = await records.GetServicePresetUsageCountsAsync(cancellationToken).ConfigureAwait(false);
        var recent = await records.FindMostRecentAsync(cancellationToken).ConfigureAwait(false);
        var candidates = allPresets.Select(p =>
        {
            var issues = ApplicationSupport.ValidateSelection(snapshot, p.ServiceId, p.TimeCategoryId);
            return new ServicePresetCandidateDto(
                p, p.IsEnabled && issues.Count == 0,
                usage.TryGetValue(p.Id, out var count) ? count : 0,
                recent?.SourceServicePresetId == p.Id, issues);
        }).OrderByDescending(x => x.IsMostRecentlyUsed)
          .ThenByDescending(x => x.UsageCount)
          .ThenBy(x => x.Preset.DisplayOrder.Value)
          .ThenBy(x => x.Preset.DisplayName, StringComparer.Ordinal)
          .ToArray();
        SaveWorkRecordCommand? suggested = recent is null ? null : new(
            null, workDate, recent.ServiceId, recent.TimeCategoryId, recent.InputMode,
            recent.InputMode == WorkInputMode.Duration ? recent.WorkMinutes : null,
            recent.StartTime, recent.InputMode == WorkInputMode.TimeRange ? recent.EndTime : null,
            recent.SourceServicePresetId, Guid.NewGuid());
        return new(workDate, new MonthSettingsDto(month, snapshot), candidates, suggested);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkRecordDto>> GetForDateAsync(DateOnly workDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<WorkRecordDto>();
        await foreach (var item in records.StreamRangeAsync(workDate, workDate, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            result.Add(item);
        return result;
    }

    /// <inheritdoc />
    public async Task<WorkRecordPreviewDto> PreviewAsync(SaveWorkRecordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await settings.GetEffectiveForMonthAsync(ApplicationSupport.ToYearMonth(command.WorkDate), cancellationToken).ConfigureAwait(false);
        var existing = command.Id is null ? null : await records.FindAsync(command.Id.Value, cancellationToken).ConfigureAwait(false);
        if (command.Id is not null && existing is null)
            return new(null, null, null, null, false,
                [ApplicationSupport.Issue("WORK_NOT_FOUND", "更新する勤務記録が見つかりませんでした。")]);
        return await PreviewCoreAsync(command, snapshot, existing, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<SaveWorkRecordResultDto> SaveAsync(SaveWorkRecordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.Id is not null) return SaveCoreAsync(command, cancellationToken);
        if (command.OperationId is not { } operationId || operationId == Guid.Empty)
            throw new ApplicationErrorException("WORK_OPERATION_ID_REQUIRED", "勤務を新規保存するための操作識別子がありません。画面を開き直して再度保存してください。");
        var pending = pendingSaves.GetOrAdd(operationId, _ => new PendingSave(command,
            new Lazy<Task<SaveWorkRecordResultDto>>(
                () => SaveCoreAsync(command, CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication)));
        if (pending.Command != command)
            throw new ApplicationErrorException("WORK_OPERATION_CONFLICT", "同じ保存操作識別子が別の勤務内容に使用されています。画面を再読み込みしてください。");
        var operation = pending.Operation.Value;
        _ = operation.ContinueWith(
            (_, state) =>
            {
                var tuple = ((WorkRecordUseCase Owner, Guid Id, PendingSave Pending))state!;
                tuple.Owner.pendingSaves.TryRemove(new KeyValuePair<Guid, PendingSave>(tuple.Id, tuple.Pending));
            }, (this, operationId, pending), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return operation.WaitAsync(cancellationToken);
    }

    private async Task<SaveWorkRecordResultDto> SaveCoreAsync(SaveWorkRecordCommand command, CancellationToken cancellationToken) =>
        await transactions.ExecuteAsync(async token =>
        {
            var snapshot = await settings.EnsureForMonthAsync(ApplicationSupport.ToYearMonth(command.WorkDate), token).ConfigureAwait(false);
            var normalizedCommand = command.Id is null ? command with { Id = new WorkRecordId(Guid.NewGuid()) } : command;
            var existing = command.Id is null ? null : await records.FindAsync(command.Id.Value, token).ConfigureAwait(false);
            if (command.Id is not null && existing is null)
                throw new ApplicationErrorException("WORK_NOT_FOUND", "更新する勤務記録が見つかりませんでした。");
            var preview = await PreviewCoreAsync(normalizedCommand, snapshot, existing, token).ConfigureAwait(false);
            if (!preview.CanSave || preview.Calculation is null || preview.NormalizedWorkMinutes is null)
                throw new ApplicationErrorException("WORK_INPUT_INVALID", "入力内容を修正してから保存してください。");
            var dto = new WorkRecordDto(
                normalizedCommand.Id!.Value, normalizedCommand.WorkDate, normalizedCommand.ServiceId,
                normalizedCommand.TimeCategoryId, normalizedCommand.InputMode, preview.NormalizedWorkMinutes.Value,
                preview.NormalizedStartTime, preview.NormalizedEndTime, normalizedCommand.SourceServicePresetId,
                existing?.SourceBasicShiftId, existing?.SourceWorkRecordId);
            if (command.Id is null)
            {
                var operationId = command.OperationId!.Value;
                var alreadySaved = await records.FindBySaveOperationIdAsync(operationId, token).ConfigureAwait(false);
                if (alreadySaved is not null) return await DuplicateResultAsync(alreadySaved, dto, snapshot, token).ConfigureAwait(false);
                if (!await records.TryInsertAsync(dto, operationId, token).ConfigureAwait(false))
                {
                    alreadySaved = await records.FindBySaveOperationIdAsync(operationId, token).ConfigureAwait(false)
                        ?? throw new ApplicationErrorException("WORK_SAVE_CONFLICT", "勤務の保存状態を確認できませんでした。日別一覧を再読み込みしてください。");
                    return await DuplicateResultAsync(alreadySaved, dto, snapshot, token).ConfigureAwait(false);
                }
            }
            else
            {
                await records.UpsertAsync(dto, token).ConfigureAwait(false);
            }
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
            return new SaveWorkRecordResultDto(dto, preview.Calculation, preview.Issues);
        }, cancellationToken).ConfigureAwait(false);

    private async Task<SaveWorkRecordResultDto> DuplicateResultAsync(WorkRecordDto saved, WorkRecordDto requested,
        SettingSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!SameInput(saved, requested))
            throw new ApplicationErrorException("WORK_OPERATION_CONFLICT", "同じ保存操作識別子が別の勤務内容に使用されています。画面を再読み込みしてください。");
        var calculation = await ApplicationSupport.CalculateAsync(saved, snapshot, holidays, calculator, cancellationToken).ConfigureAwait(false);
        return new(saved, calculation, ApplicationSupport.CalculationIssues(calculation));
    }

    private static bool SameInput(WorkRecordDto left, WorkRecordDto right) =>
        left.WorkDate == right.WorkDate && left.ServiceId == right.ServiceId &&
        left.TimeCategoryId == right.TimeCategoryId && left.InputMode == right.InputMode &&
        left.WorkMinutes == right.WorkMinutes && left.StartTime == right.StartTime && left.EndTime == right.EndTime &&
        left.SourceServicePresetId == right.SourceServicePresetId;

    private sealed record PendingSave(
        SaveWorkRecordCommand Command,
        Lazy<Task<SaveWorkRecordResultDto>> Operation);

    /// <inheritdoc />
    public async Task DeleteAsync(WorkRecordId id, CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidateId(id.Value, nameof(id));
        cancellationToken.ThrowIfCancellationRequested();
        await transactions.ExecuteAsync(async token =>
        {
            if (await records.FindAsync(id, token).ConfigureAwait(false) is null)
                throw new ApplicationErrorException("WORK_NOT_FOUND", "削除する勤務記録が見つかりませんでした。");
            await records.DeleteAsync(id, token).ConfigureAwait(false);
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CopyDayPreviewDto> PreviewCopyDayAsync(DateOnly sourceDate, DateOnly targetDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = await GetForDateAsync(sourceDate, cancellationToken).ConfigureAwait(false);
        var target = await GetForDateAsync(targetDate, cancellationToken).ConfigureAwait(false);
        var issues = new List<IssueDto>();
        if (sourceDate == targetDate) issues.Add(ApplicationSupport.Issue("COPY_DAY_SAME_DATE", "複製先には別の日付を指定してください。", "TargetDate"));
        if (source.Count == 0) issues.Add(ApplicationSupport.Issue("COPY_DAY_SOURCE_EMPTY", "複製元の日付に勤務記録がありません。", "SourceDate"));
        if (target.Count != 0) issues.Add(ApplicationSupport.Issue("COPY_DAY_TARGET_HAS_RECORDS", "複製先には既存の勤務記録があります。重複しないか確認してください。"));
        if (source.Count != 0 && sourceDate != targetDate)
        {
            var snapshot = await settings.GetEffectiveForMonthAsync(ApplicationSupport.ToYearMonth(targetDate), cancellationToken).ConfigureAwait(false);
            var calendar = await holidays.GetAsync(snapshot.HolidayCalendarVersionId, cancellationToken).ConfigureAwait(false);
            foreach (var value in source)
            {
                issues.AddRange(ApplicationSupport.ValidateSelection(snapshot, value.ServiceId, value.TimeCategoryId));
                if (ApplicationSupport.RequiresStartTime(snapshot, value.ServiceId, targetDate, calendar) && value.StartTime is null)
                    issues.Add(ApplicationSupport.Issue("COPY_DAY_START_REQUIRED_FOR_PREMIUM", "複製先では時刻条件付き割増が適用されるため、開始時刻のない勤務を複製できません。"));
                else
                {
                    var candidate = value with { Id = new WorkRecordId(Guid.NewGuid()), WorkDate = targetDate };
                    var calculation = calculator.Calculate(new WorkSalaryCalculationRequest(ApplicationSupport.ToDomain(candidate),
                        ApplicationSupport.ForCalculationDate(snapshot, targetDate, calendar), calendar));
                    issues.AddRange(ApplicationSupport.CalculationIssues(calculation));
                }
            }
        }
        var sourceMonth = ApplicationSupport.ToYearMonth(sourceDate);
        var targetMonth = ApplicationSupport.ToYearMonth(targetDate);
        return new(sourceDate, targetDate, source.Count, target.Count, sourceMonth, targetMonth, sourceMonth != targetMonth, issues);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SaveWorkRecordResultDto>> CopyDayAsync(DateOnly sourceDate, DateOnly targetDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceDate == targetDate) throw new ApplicationErrorException("COPY_DAY_SAME_DATE", "複製先には別の日付を指定してください。", "TargetDate");
        return await transactions.ExecuteAsync(async token =>
        {
            var source = new List<WorkRecordDto>();
            await foreach (var item in records.StreamRangeAsync(sourceDate, sourceDate, token).WithCancellation(token).ConfigureAwait(false)) source.Add(item);
            if (source.Count == 0) throw new ApplicationErrorException("COPY_DAY_SOURCE_EMPTY", "複製元の日付に勤務記録がありません。");
            var snapshot = await settings.EnsureForMonthAsync(ApplicationSupport.ToYearMonth(targetDate), token).ConfigureAwait(false);
            var calendar = await holidays.GetAsync(snapshot.HolidayCalendarVersionId, token).ConfigureAwait(false);
            var results = new List<SaveWorkRecordResultDto>(source.Count);
            foreach (var old in source)
            {
                var validation = ApplicationSupport.ValidateSelection(snapshot, old.ServiceId, old.TimeCategoryId);
                if (validation.Count != 0)
                    throw new ApplicationErrorException(validation[0].Code, validation[0].Message, validation[0].Field);
                if (ApplicationSupport.RequiresStartTime(snapshot, old.ServiceId, targetDate, calendar) && old.StartTime is null)
                    throw new ApplicationErrorException("COPY_DAY_START_REQUIRED_FOR_PREMIUM", "複製先では時刻条件付き割増が適用されるため、開始時刻のない勤務を複製できません。");
                var copied = old with { Id = new WorkRecordId(Guid.NewGuid()), WorkDate = targetDate, SourceBasicShiftId = null, SourceWorkRecordId = old.Id };
                var calculation = calculator.Calculate(new WorkSalaryCalculationRequest(ApplicationSupport.ToDomain(copied),
                    ApplicationSupport.ForCalculationDate(snapshot, targetDate, calendar), calendar));
                await records.UpsertAsync(copied, token).ConfigureAwait(false);
                results.Add(new(copied, calculation, ApplicationSupport.CalculationIssues(calculation)));
            }
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
            return (IReadOnlyList<SaveWorkRecordResultDto>)results;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkRecordPreviewDto> PreviewCoreAsync(
        SaveWorkRecordCommand command,
        SettingSnapshot snapshot,
        WorkRecordDto? existing,
        CancellationToken cancellationToken)
    {
        var keepsExistingSelection = existing is not null && existing.ServiceId == command.ServiceId &&
            existing.TimeCategoryId == command.TimeCategoryId;
        var selectionIssues = ApplicationSupport.ValidateSelection(snapshot, command.ServiceId, command.TimeCategoryId, keepsExistingSelection);
        var calendar = await holidays.GetAsync(snapshot.HolidayCalendarVersionId, cancellationToken).ConfigureAwait(false);
        var normalized = ApplicationSupport.Normalize(command.InputMode, command.WorkMinutes, command.StartTime, command.EndTime,
            ApplicationSupport.RequiresStartTime(snapshot, command.ServiceId, command.WorkDate, calendar));
        var errors = selectionIssues.Concat(normalized.Issues).ToList();
        if (errors.Count != 0 || normalized.Minutes is null)
            return new(normalized.Minutes, normalized.Start, normalized.End, null, false, errors);
        var dto = new WorkRecordDto(
            command.Id ?? new WorkRecordId(Guid.NewGuid()), command.WorkDate, command.ServiceId, command.TimeCategoryId,
            command.InputMode, normalized.Minutes.Value, normalized.Start, normalized.End, command.SourceServicePresetId, null, null);
        var calculation = await ApplicationSupport.CalculateAsync(dto, snapshot, holidays, calculator, cancellationToken).ConfigureAwait(false);
        errors.AddRange(ApplicationSupport.CalculationIssues(calculation));
        return new(normalized.Minutes, normalized.Start, normalized.End, calculation, true, errors);
    }
}
