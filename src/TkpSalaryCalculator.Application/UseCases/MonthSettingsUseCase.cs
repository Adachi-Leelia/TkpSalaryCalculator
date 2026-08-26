using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Internal;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;
using System.Security.Cryptography;
using System.Text;

namespace TkpSalaryCalculator.Application.UseCases;

/// <summary>不変スナップショットの影響確認、対象月だけの置換、および前月コピーを実装します。</summary>
/// <remarks>必要なポートとドメインサービスを指定して生成します。</remarks>
public sealed class MonthSettingsUseCase(ISettingSnapshotRepository settings, IWorkRecordRepository records,
    IHolidayCalendarRepository holidays, ISalaryCalculator calculator, ITransactionRunner transactions,
    IAppMetadataRepository metadata, IUtcClock clock, IServicePresetRepository? presets = null) : IMonthSettingsUseCase
{
    private readonly ISettingSnapshotRepository settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IWorkRecordRepository records = records ?? throw new ArgumentNullException(nameof(records));
    private readonly IHolidayCalendarRepository holidays = holidays ?? throw new ArgumentNullException(nameof(holidays));
    private readonly ISalaryCalculator calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
    private readonly ITransactionRunner transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    private readonly IAppMetadataRepository metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IServicePresetRepository presets = presets ?? EmptyServicePresetRepository.Instance;


    /// <inheritdoc />
    public async Task<MonthSettingsDto> GetAsync(YearMonth yearMonth, CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidateYearMonth(yearMonth, nameof(yearMonth));
        cancellationToken.ThrowIfCancellationRequested();
        return new(yearMonth, await settings.GetEffectiveForMonthAsync(yearMonth, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<SettingReplacementPreviewDto> PreviewReplacementAsync(YearMonth yearMonth,
        SettingSnapshotReplacementDto replacement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ApplicationSupport.ValidateYearMonth(yearMonth, nameof(yearMonth));
        cancellationToken.ThrowIfCancellationRequested();
        var current = await settings.GetEffectiveForMonthAsync(yearMonth, cancellationToken).ConfigureAwait(false);
        return await PreviewCoreAsync(yearMonth, current, replacement, current.HolidayCalendarVersionId, null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<MonthSettingsDto> CloneAndReplaceAsync(YearMonth yearMonth,
        SettingSnapshotReplacementDto replacement, SettingReplacementConfirmationToken confirmationToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(confirmationToken);
        ApplicationSupport.ValidateYearMonth(yearMonth, nameof(yearMonth));
        cancellationToken.ThrowIfCancellationRequested();
        return await transactions.ExecuteAsync(async token =>
        {
            var currentBeforeWrite = await settings.GetEffectiveForMonthAsync(yearMonth, token).ConfigureAwait(false);
            var validatedReplacement = ValidateReplacement(currentBeforeWrite, replacement, currentBeforeWrite.HolidayCalendarVersionId);
            await ValidateConfirmationAsync(yearMonth, validatedReplacement, currentBeforeWrite.HolidayCalendarVersionId,
                confirmationToken, null, token).ConfigureAwait(false);
            var current = currentBeforeWrite;
            var result = await settings.TryCloneAndReplaceMonthSnapshotAsync(yearMonth, confirmationToken.TargetSnapshotId,
                validatedReplacement, current.HolidayCalendarVersionId, clock.UtcNow.ToUniversalTime(), token).ConfigureAwait(false)
                ?? throw ChangedSincePreview();
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
            return new MonthSettingsDto(yearMonth, result);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<MonthSettingsDto> CloneAndReplaceWithServicePresetAsync(YearMonth yearMonth,
        SettingSnapshotReplacementDto replacement, SettingReplacementConfirmationToken confirmationToken,
        ServicePresetChangeCommand presetChange, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(confirmationToken);
        ArgumentNullException.ThrowIfNull(presetChange);
        if (presetChange.Save is not null && presetChange.DeleteId is not null)
            throw new ArgumentException("入力候補の保存と削除は同時に指定できません。", nameof(presetChange));
        ApplicationSupport.ValidateYearMonth(yearMonth, nameof(yearMonth));
        cancellationToken.ThrowIfCancellationRequested();

        return await transactions.ExecuteAsync(async token =>
        {
            var current = await settings.GetEffectiveForMonthAsync(yearMonth, token).ConfigureAwait(false);
            var validatedReplacement = ValidateReplacement(current, replacement, current.HolidayCalendarVersionId);
            await ValidateConfirmationAsync(yearMonth, validatedReplacement, current.HolidayCalendarVersionId,
                confirmationToken, null, token).ConfigureAwait(false);
            var result = await settings.TryCloneAndReplaceMonthSnapshotAsync(yearMonth, confirmationToken.TargetSnapshotId,
                validatedReplacement, current.HolidayCalendarVersionId, clock.UtcNow.ToUniversalTime(), token).ConfigureAwait(false)
                ?? throw ChangedSincePreview();

            if (presetChange.Save is { } save)
            {
                var preset = CreatePreset(save, validatedReplacement);
                await presets.UpsertAsync(preset, token).ConfigureAwait(false);
            }
            else if (presetChange.DeleteId is { } deleteId)
            {
                ApplicationSupport.ValidateId(deleteId.Value, nameof(presetChange.DeleteId));
                await presets.DeleteAsync(deleteId, token).ConfigureAwait(false);
            }

            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
            return new MonthSettingsDto(yearMonth, result);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SettingReplacementPreviewDto> PreviewCopyPreviousMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidateYearMonth(yearMonth, nameof(yearMonth));
        cancellationToken.ThrowIfCancellationRequested();
        var current = await settings.GetEffectiveForMonthAsync(yearMonth, cancellationToken).ConfigureAwait(false);
        var previous = await settings.GetEffectiveForMonthAsync(yearMonth.AddMonths(-1), cancellationToken).ConfigureAwait(false);
        var replacement = ToReplacement(previous);
        var latestHoliday = await holidays.GetLatestVerifiedVersionIdAsync(cancellationToken).ConfigureAwait(false);
        return await PreviewCoreAsync(yearMonth, current, replacement, latestHoliday, previous.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<MonthSettingsDto> CopyPreviousMonthAsync(YearMonth yearMonth,
        SettingReplacementConfirmationToken confirmationToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmationToken);
        ApplicationSupport.ValidateYearMonth(yearMonth, nameof(yearMonth));
        if (confirmationToken.SourceSnapshotId is null) throw ChangedSincePreview();
        cancellationToken.ThrowIfCancellationRequested();
        return await transactions.ExecuteAsync(async token =>
        {
            var latestHoliday = await holidays.GetLatestVerifiedVersionIdAsync(token).ConfigureAwait(false);
            var previous = await settings.GetEffectiveForMonthAsync(yearMonth.AddMonths(-1), token).ConfigureAwait(false);
            var current = await settings.GetEffectiveForMonthAsync(yearMonth, token).ConfigureAwait(false);
            var copyReplacement = ValidateReplacement(current, ToReplacement(previous), latestHoliday);
            await ValidateConfirmationAsync(yearMonth, copyReplacement, latestHoliday,
                confirmationToken, confirmationToken.SourceSnapshotId, token).ConfigureAwait(false);
            var result = await settings.TryCloneAndReplaceMonthSnapshotAsync(yearMonth, confirmationToken.TargetSnapshotId,
                copyReplacement, latestHoliday, clock.UtcNow.ToUniversalTime(), token).ConfigureAwait(false)
                ?? throw ChangedSincePreview();
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
            return new MonthSettingsDto(yearMonth, result);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SettingReplacementPreviewDto> PreviewCoreAsync(YearMonth month, SettingSnapshot current,
        SettingSnapshotReplacementDto replacement, HolidayCalendarVersionId holidayVersionId,
        SettingSnapshotId? sourceSnapshotId, CancellationToken cancellationToken)
    {
        var monthRecords = await LoadMonthRecordsAsync(month, cancellationToken).ConfigureAwait(false);
        SettingSnapshot candidate;
        try
        {
            ArgumentNullException.ThrowIfNull(replacement.Services);
            ArgumentNullException.ThrowIfNull(replacement.TimeCategories);
            ArgumentNullException.ThrowIfNull(replacement.Rates);
            ArgumentNullException.ThrowIfNull(replacement.Premiums);
            ArgumentNullException.ThrowIfNull(replacement.CountBonuses);
            candidate = new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), current.Id, holidayVersionId,
                current.SchemaVersion, DateTimeOffset.UnixEpoch, replacement.Services, replacement.TimeCategories,
                replacement.Rates, replacement.Premiums, replacement.CountBonuses);
        }
        catch (ArgumentException)
        {
            var invalidConfirmation = new SettingReplacementConfirmationToken(month, current.Id, sourceSnapshotId,
                Fingerprint(monthRecords), string.Empty, holidayVersionId);
            return new(month, invalidConfirmation, 0, new YenAmount(0), new YenAmount(0), 0,
                [ApplicationSupport.Issue("SETTINGS_REPLACEMENT_INVALID", "設定内容に重複または不整合があります。各項目を確認してください。")]);
        }
        var normalizedReplacement = ToReplacement(candidate);
        var confirmation = new SettingReplacementConfirmationToken(month, current.Id, sourceSnapshotId,
            Fingerprint(monthRecords), Fingerprint(normalizedReplacement), holidayVersionId);

        var calendars = await holidays.GetManyAsync(
            [current.HolidayCalendarVersionId, candidate.HolidayCalendarVersionId], cancellationToken).ConfigureAwait(false);
        var currentCalendar = calendars[current.HolidayCalendarVersionId];
        var candidateCalendar = calendars[candidate.HolidayCalendarVersionId];
        return await Task.Run(() =>
        {
            var calculationSnapshots = monthRecords.Select(x => x.WorkDate).Distinct().ToDictionary(
                date => date,
                date => (
                    Current: ApplicationSupport.ForCalculationDate(current, date, currentCalendar),
                    Candidate: ApplicationSupport.ForCalculationDate(candidate, date, candidateCalendar)));
            long currentTotal = 0;
            long replacementTotal = 0;
            var affected = 0;
            var uncalculated = 0;
            foreach (var value in monthRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var domain = ApplicationSupport.ToDomain(value);
                var snapshots = calculationSnapshots[value.WorkDate];
                var before = calculator.Calculate(new WorkSalaryCalculationRequest(domain,
                    snapshots.Current, currentCalendar));
                var after = calculator.Calculate(new WorkSalaryCalculationRequest(domain,
                    snapshots.Candidate, candidateCalendar));
                currentTotal += before.Total?.Value ?? 0;
                replacementTotal += after.Total?.Value ?? 0;
                if (!Equals(before, after)) affected++;
                if (after.Status == SalaryCalculationStatus.Uncalculated) uncalculated++;
            }
            return new SettingReplacementPreviewDto(month, confirmation, affected, new YenAmount(currentTotal),
                new YenAmount(replacementTotal), uncalculated, []);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateConfirmationAsync(YearMonth month, SettingSnapshotReplacementDto replacement,
        HolidayCalendarVersionId holidayVersionId, SettingReplacementConfirmationToken confirmation,
        SettingSnapshotId? expectedSourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        var current = await settings.GetEffectiveForMonthAsync(month, cancellationToken).ConfigureAwait(false);
        var source = expectedSourceId is null
            ? null
            : await settings.GetEffectiveForMonthAsync(month.AddMonths(-1), cancellationToken).ConfigureAwait(false);
        var values = await LoadMonthRecordsAsync(month, cancellationToken).ConfigureAwait(false);
        if (confirmation.TargetMonth != month ||
            current.Id != confirmation.TargetSnapshotId ||
            source?.Id != confirmation.SourceSnapshotId ||
            confirmation.HolidayCalendarVersionId != holidayVersionId ||
            !StringComparer.Ordinal.Equals(Fingerprint(replacement), confirmation.ReplacementFingerprint) ||
            !StringComparer.Ordinal.Equals(Fingerprint(values), confirmation.WorkRecordsFingerprint))
            throw ChangedSincePreview();
    }

    private async Task<IReadOnlyList<WorkRecordDto>> LoadMonthRecordsAsync(YearMonth month, CancellationToken cancellationToken)
    {
        var start = new DateOnly(month.Year, month.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var result = new List<WorkRecordDto>();
        await foreach (var value in records.StreamRangeAsync(start, end, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            result.Add(value);
        return result;
    }

    private static string Fingerprint(IReadOnlyList<WorkRecordDto> values)
    {
        var builder = new StringBuilder();
        foreach (var value in values.OrderBy(x => x.WorkDate).ThenBy(x => x.Id.Value))
            builder.Append(value.Id.Value).Append('|').Append(value.WorkDate.DayNumber).Append('|')
                .Append(value.ServiceId.Value).Append('|').Append(value.TimeCategoryId?.Value).Append('|')
                .Append((int)value.InputMode).Append('|').Append(value.WorkMinutes.Value).Append('|')
                .Append(value.StartTime?.Value).Append('|').Append(value.EndTime?.Value).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string Fingerprint(SettingSnapshotReplacementDto value)
    {
        var builder = new StringBuilder();
        foreach (var item in value.Services.OrderBy(x => x.Id.Value))
            Append(builder, "S", item.Id.Value, item.DisplayName, item.DisplayOrder.Value, item.IsEnabled);
        foreach (var item in value.TimeCategories.OrderBy(x => x.Id.Value))
            Append(builder, "T", item.Id.Value, item.ServiceId.Value, item.DisplayName,
                item.StandardMinutes.Value, item.DisplayOrder.Value, item.IsEnabled);
        foreach (var item in value.Rates.OrderBy(x => x.ServiceId.Value).ThenBy(x => x.TimeCategoryId?.Value))
            Append(builder, "R", item.ServiceId.Value, item.TimeCategoryId?.Value, (int)item.RateType, item.Amount.Value);
        foreach (var item in value.Premiums.OrderBy(x => x.Id.Value))
        {
            Append(builder, "P", item.Id.Value, item.DisplayName, (int)item.CalculationType,
                item.Percentage?.Value, item.Amount?.Value, item.StartTime?.Value, item.EndTime?.Value,
                item.UsesNationalHolidays, item.IsEnabled);
            foreach (var weekday in item.Weekdays.OrderBy(x => x)) Append(builder, "PW", (int)weekday);
            foreach (var date in item.Dates.OrderBy(x => x)) Append(builder, "PD", date.DayNumber);
            foreach (var service in item.ServiceIds.OrderBy(x => x.Value)) Append(builder, "PS", service.Value);
        }
        foreach (var item in value.CountBonuses.OrderBy(x => x.Id.Value))
        {
            Append(builder, "B", item.Id.Value, item.DisplayName, item.Amount.Value, item.IsEnabled);
            foreach (var service in item.ServiceIds.OrderBy(x => x.Value)) Append(builder, "BS", service.Value);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, params object?[] values)
    {
        foreach (var value in values)
        {
            var text = value?.ToString() ?? "<null>";
            builder.Append(text.Length).Append(':').Append(text).Append('|');
        }
        builder.Append(';');
    }

    private static ApplicationErrorException ChangedSincePreview()
    {
        return new(
        "SETTINGS_PREVIEW_STALE", "確認後に対象月の設定または勤務が変更されました。影響をもう一度確認してください。");
    }

    private static ServicePresetDto CreatePreset(SaveServicePresetCommand command,
        SettingSnapshotReplacementDto replacement)
    {
        ApplicationSupport.ValidateId(command.ServiceId.Value, nameof(command.ServiceId));
        if (command.TimeCategoryId is { } categoryId)
            ApplicationSupport.ValidateId(categoryId.Value, nameof(command.TimeCategoryId));
        var name = command.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ApplicationErrorException("PRESET_NAME_REQUIRED", "入力候補名を入力してください。", "DisplayName");
        if (command.DefaultWorkMinutes.Value is < 1 or > 1440)
            throw new ApplicationErrorException("PRESET_MINUTES_INVALID", "標準勤務時間は1分から24時間以内で入力してください。", "DefaultWorkMinutes");
        if (!replacement.Services.Any(value => value.Id == command.ServiceId) ||
            command.TimeCategoryId is { } timeCategoryId && !replacement.TimeCategories.Any(value =>
                value.Id == timeCategoryId && value.ServiceId == command.ServiceId))
            throw new ApplicationErrorException("PRESET_SETTING_REFERENCE_INVALID", "入力候補のサービス設定が不正です。");

        return new ServicePresetDto(command.Id ?? new ServicePresetId(Guid.NewGuid()), name,
            command.ServiceId, command.TimeCategoryId, command.DefaultWorkMinutes, command.DisplayOrder, command.IsEnabled);
    }


    private static SettingSnapshotReplacementDto ValidateReplacement(SettingSnapshot current, SettingSnapshotReplacementDto replacement,
        HolidayCalendarVersionId holidayVersionId)
    {
        try
        {
            var candidate = new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), current.Id, holidayVersionId,
                current.SchemaVersion, DateTimeOffset.UnixEpoch, replacement.Services, replacement.TimeCategories,
                replacement.Rates, replacement.Premiums, replacement.CountBonuses);
            return ToReplacement(candidate);
        }
        catch (ArgumentException exception)
        {
            throw new ApplicationErrorException("SETTINGS_REPLACEMENT_INVALID",
                "設定内容に重複または不整合があります。各項目を確認してください。", innerException: exception);
        }
    }

    private static SettingSnapshotReplacementDto ToReplacement(SettingSnapshot value)
    {
        return new(
        value.Services, value.TimeCategories, value.Rates, value.Premiums, value.CountBonuses);
    }

    private sealed class EmptyServicePresetRepository : IServicePresetRepository
    {
        public static EmptyServicePresetRepository Instance { get; } = new();
        public Task<IReadOnlyList<ServicePresetDto>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServicePresetDto>>([]);
        public Task UpsertAsync(ServicePresetDto preset, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("サービス入力候補リポジトリが登録されていません。");
        public Task DeleteAsync(ServicePresetId id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("サービス入力候補リポジトリが登録されていません。");
    }

}
