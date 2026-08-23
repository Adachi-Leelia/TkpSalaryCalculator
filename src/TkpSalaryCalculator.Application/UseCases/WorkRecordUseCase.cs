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
/// <remarks>必要なポートとドメインサービスを指定して生成します。</remarks>
public sealed class WorkRecordUseCase(
    IWorkRecordRepository records,
    ISettingSnapshotRepository settings,
    IServicePresetRepository presets,
    IHolidayCalendarRepository holidays,
    ISalaryCalculator calculator,
    ITransactionRunner transactions,
    IAppMetadataRepository metadata,
    IUtcClock clock) : IWorkRecordUseCase
{
    private readonly IWorkRecordRepository records = records ?? throw new ArgumentNullException(nameof(records));
    private readonly ISettingSnapshotRepository settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IServicePresetRepository presets = presets ?? throw new ArgumentNullException(nameof(presets));
    private readonly IHolidayCalendarRepository holidays = holidays ?? throw new ArgumentNullException(nameof(holidays));
    private readonly ISalaryCalculator calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
    private readonly ITransactionRunner transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    private readonly IAppMetadataRepository metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ConcurrentDictionary<Guid, PendingSave> pendingSaves = new();

    /// <inheritdoc />
    public async Task<WorkEditorScreenDto> GetEditorScreenAsync(
        DateOnly workDate,
        WorkRecordId? workRecordId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (workRecordId is { } id) ApplicationSupport.ValidateId(id.Value, nameof(workRecordId));

        var month = ApplicationSupport.ToYearMonth(workDate);
        var snapshot = await settings.GetEffectiveForMonthAsync(month, cancellationToken).ConfigureAwait(false);
        var allPresets = await presets.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var history = await records.GetInputHistoryAsync(cancellationToken).ConfigureAwait(false);
        var existing = workRecordId is null
            ? null
            : await records.FindAsync(workRecordId.Value, cancellationToken).ConfigureAwait(false);
        var calendar = await holidays.GetAsync(snapshot.HolidayCalendarVersionId, cancellationToken).ConfigureAwait(false);

        return new(
            BuildInputOptions(workDate, snapshot, allPresets, history),
            existing,
            calendar);
    }


    /// <inheritdoc />
    public async Task<MonthSettingsDto> GetSettingsForDateAsync(
        DateOnly workDate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var month = ApplicationSupport.ToYearMonth(workDate);
        var snapshot = await settings.GetEffectiveForMonthAsync(month, cancellationToken).ConfigureAwait(false);
        return new MonthSettingsDto(month, snapshot);
    }

    /// <inheritdoc />
    public async Task<WorkInputOptionsDto> GetInputOptionsAsync(DateOnly workDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await settings.GetEffectiveForMonthAsync(ApplicationSupport.ToYearMonth(workDate), cancellationToken)
            .ConfigureAwait(false);
        var allPresets = await presets.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var history = await records.GetInputHistoryAsync(cancellationToken).ConfigureAwait(false);
        return BuildInputOptions(workDate, snapshot, allPresets, history);
    }

    private static WorkInputOptionsDto BuildInputOptions(
        DateOnly workDate,
        SettingSnapshot snapshot,
        IReadOnlyList<ServicePresetDto> allPresets,
        WorkInputHistory history)
    {
        var monthSettings = new MonthSettingsDto(ApplicationSupport.ToYearMonth(workDate), snapshot);
        var usage = history.ServicePresetUsageCounts;
        var recent = history.MostRecent;
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
        return new(workDate, monthSettings, candidates, suggested);
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
        var calendar = await holidays.GetAsync(snapshot.HolidayCalendarVersionId, cancellationToken).ConfigureAwait(false);
        return PreviewCore(command, snapshot, existing, calendar);
    }

    /// <inheritdoc />
    public Task<WorkRecordPreviewDto> PreviewForEditorAsync(
        SaveWorkRecordCommand command,
        WorkEditorScreenDto screen,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(screen);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.WorkDate != screen.InputOptions.WorkDate)
            throw new ArgumentException("画面データと同じ勤務日を指定してください。", nameof(command));
        if (command.Id is not null && screen.ExistingRecord?.Id != command.Id)
            return Task.FromResult(new WorkRecordPreviewDto(null, null, null, null, false,
                [ApplicationSupport.Issue("WORK_NOT_FOUND", "更新する勤務記録が見つかりませんでした。")]));

        return Task.FromResult(PreviewCore(
            command,
            screen.InputOptions.Settings.Snapshot,
            screen.ExistingRecord,
            screen.HolidayCalendar));
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
                var (Owner, Id, Pending) = ((WorkRecordUseCase Owner, Guid Id, PendingSave Pending))state!;
                Owner.pendingSaves.TryRemove(new KeyValuePair<Guid, PendingSave>(Id, Pending));
            }, (this, operationId, pending), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return operation.WaitAsync(cancellationToken);
    }

    private async Task<SaveWorkRecordResultDto> SaveCoreAsync(SaveWorkRecordCommand command, CancellationToken cancellationToken)
    {
        return await transactions.ExecuteAsync(async token =>
        {
            var snapshot = await settings.EnsureForMonthAsync(ApplicationSupport.ToYearMonth(command.WorkDate), token).ConfigureAwait(false);
            var normalizedCommand = command.Id is null ? command with { Id = new WorkRecordId(Guid.NewGuid()) } : command;
            var existing = command.Id is null ? null : await records.FindAsync(command.Id.Value, token).ConfigureAwait(false);
            if (command.Id is not null && existing is null)
                throw new ApplicationErrorException("WORK_NOT_FOUND", "更新する勤務記録が見つかりませんでした。");
            var calendar = await holidays.GetAsync(snapshot.HolidayCalendarVersionId, token).ConfigureAwait(false);
            var preview = PreviewCore(normalizedCommand, snapshot, existing, calendar);
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
                if (alreadySaved is not null) return DuplicateResult(alreadySaved, dto, snapshot, calendar);
                if (!await records.TryInsertAsync(dto, operationId, token).ConfigureAwait(false))
                {
                    alreadySaved = await records.FindBySaveOperationIdAsync(operationId, token).ConfigureAwait(false)
                        ?? throw new ApplicationErrorException("WORK_SAVE_CONFLICT", "勤務の保存状態を確認できませんでした。日別一覧を再読み込みしてください。");
                    return DuplicateResult(alreadySaved, dto, snapshot, calendar);
                }
            }
            else
            {
                await records.UpsertAsync(dto, token).ConfigureAwait(false);
            }
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
            return new SaveWorkRecordResultDto(dto, preview.Calculation, preview.Issues);
        }, cancellationToken).ConfigureAwait(false);
    }


    private SaveWorkRecordResultDto DuplicateResult(
        WorkRecordDto saved,
        WorkRecordDto requested,
        SettingSnapshot snapshot,
        HolidayCalendar calendar)
    {
        if (!SameInput(saved, requested))
            throw new ApplicationErrorException("WORK_OPERATION_CONFLICT", "同じ保存操作識別子が別の勤務内容に使用されています。画面を再読み込みしてください。");
        var calculation = ApplicationSupport.Calculate(saved, snapshot, calendar, calculator);
        return new(saved, calculation, ApplicationSupport.CalculationIssues(calculation));
    }

    private static bool SameInput(WorkRecordDto left, WorkRecordDto right)
    {
        return left.WorkDate == right.WorkDate && left.ServiceId == right.ServiceId &&
        left.TimeCategoryId == right.TimeCategoryId && left.InputMode == right.InputMode &&
        left.WorkMinutes == right.WorkMinutes && left.StartTime == right.StartTime && left.EndTime == right.EndTime &&
        left.SourceServicePresetId == right.SourceServicePresetId;
    }


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
        else if (sourceDate > targetDate) issues.Add(ApplicationSupport.Issue("COPY_DAY_SOURCE_MUST_BE_PAST", "複製元には複製先より過去の日付を指定してください。", "SourceDate"));
        if (source.Count == 0) issues.Add(ApplicationSupport.Issue("COPY_DAY_SOURCE_EMPTY", "複製元の日付に勤務記録がありません。", "SourceDate"));
        if (target.Count != 0) issues.Add(ApplicationSupport.Issue("COPY_DAY_TARGET_HAS_RECORDS", "複製先には既存の勤務記録があります。重複しないか確認してください。"));
        var targetMonth = ApplicationSupport.ToYearMonth(targetDate);
        var (snapshot, confirmationToken) = await ResolveCopyTargetSettingsAsync(
            sourceDate, targetDate, targetMonth, target.Count, cancellationToken).ConfigureAwait(false);
        if (source.Count != 0 && sourceDate < targetDate)
        {
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
        return new(sourceDate, targetDate, source.Count, target.Count, sourceMonth, targetMonth, sourceMonth != targetMonth, issues, confirmationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SaveWorkRecordResultDto>> CopyDayAsync(DateOnly sourceDate, DateOnly targetDate,
        CopyDayConfirmationToken confirmationToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceDate == targetDate) throw new ApplicationErrorException("COPY_DAY_SAME_DATE", "複製先には別の日付を指定してください。", "TargetDate");
        if (sourceDate > targetDate) throw new ApplicationErrorException("COPY_DAY_SOURCE_MUST_BE_PAST", "複製元には複製先より過去の日付を指定してください。", "SourceDate");
        var targetMonth = ApplicationSupport.ToYearMonth(targetDate);
        if (confirmationToken.SourceDate != sourceDate || confirmationToken.TargetDate != targetDate)
            throw CopyDayPreviewChanged();
        return await transactions.ExecuteAsync(async token =>
        {
            var source = new List<WorkRecordDto>();
            await foreach (var item in records.StreamRangeAsync(sourceDate, sourceDate, token).WithCancellation(token).ConfigureAwait(false)) source.Add(item);
            if (source.Count == 0) throw new ApplicationErrorException("COPY_DAY_SOURCE_EMPTY", "複製元の日付に勤務記録がありません。");
            var targetCount = 0;
            await foreach (var _ in records.StreamRangeAsync(targetDate, targetDate, token).WithCancellation(token).ConfigureAwait(false)) targetCount++;
            if (targetCount != confirmationToken.ExpectedTargetExistingWorkRecordCount)
                throw CopyDayPreviewChanged();
            var snapshot = await settings.TryEnsureForMonthAsync(targetMonth,
                confirmationToken.ExpectedEffectiveSnapshotId, confirmationToken.ExpectedHolidayCalendarVersionId, token)
                .ConfigureAwait(false) ?? throw CopyDayPreviewChanged();
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

    private async Task<(SettingSnapshot Snapshot, CopyDayConfirmationToken ConfirmationToken)> ResolveCopyTargetSettingsAsync(
        DateOnly sourceDate, DateOnly targetDate, YearMonth targetMonth, int targetExistingWorkRecordCount,
        CancellationToken cancellationToken)
    {
        var existing = await settings.FindForMonthAsync(targetMonth, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return (existing, new(sourceDate, targetDate, targetExistingWorkRecordCount, existing.Id, existing.HolidayCalendarVersionId));

        var effective = await settings.GetEffectiveForMonthAsync(targetMonth, cancellationToken).ConfigureAwait(false);
        var latestHoliday = await holidays.GetLatestVerifiedVersionIdAsync(cancellationToken).ConfigureAwait(false);
        var previewSnapshot = effective.HolidayCalendarVersionId == latestHoliday
            ? effective
            : new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), effective.Id, latestHoliday,
                effective.SchemaVersion, effective.CreatedAtUtc, effective.Services, effective.TimeCategories,
                effective.Rates, effective.Premiums, effective.CountBonuses);
        return (previewSnapshot, new(sourceDate, targetDate, targetExistingWorkRecordCount, effective.Id, latestHoliday));
    }

    private static ApplicationErrorException CopyDayPreviewChanged() =>
        new("COPY_DAY_PREVIEW_STALE", "複製前の設定が変更されました。内容を確認してからもう一度複製してください。");

    private WorkRecordPreviewDto PreviewCore(
        SaveWorkRecordCommand command,
        SettingSnapshot snapshot,
        WorkRecordDto? existing,
        HolidayCalendar calendar)
    {
        var keepsExistingSelection = existing is not null && existing.ServiceId == command.ServiceId &&
            existing.TimeCategoryId == command.TimeCategoryId;
        var selectionIssues = ApplicationSupport.ValidateSelection(snapshot, command.ServiceId, command.TimeCategoryId, keepsExistingSelection);
        var (Minutes, Start, End, Issues) = ApplicationSupport.Normalize(command.InputMode, command.WorkMinutes, command.StartTime, command.EndTime,
            ApplicationSupport.RequiresStartTime(snapshot, command.ServiceId, command.WorkDate, calendar));
        var errors = selectionIssues.Concat(Issues).ToList();
        if (errors.Count != 0 || Minutes is null)
            return new(Minutes, Start, End, null, false, errors);
        var dto = new WorkRecordDto(
            command.Id ?? new WorkRecordId(Guid.NewGuid()), command.WorkDate, command.ServiceId, command.TimeCategoryId,
            command.InputMode, Minutes.Value, Start, End, command.SourceServicePresetId, null, null);
        var calculation = ApplicationSupport.Calculate(dto, snapshot, calendar, calculator);
        errors.AddRange(ApplicationSupport.CalculationIssues(calculation));
        return new(Minutes, Start, End, calculation, true, errors);
    }
}
