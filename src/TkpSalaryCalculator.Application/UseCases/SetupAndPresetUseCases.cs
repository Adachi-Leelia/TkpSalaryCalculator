using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Internal;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.UseCases;

/// <summary>再開可能な初期設定の状態管理と完了条件の検証を実装します。</summary>
public sealed class InitialSetupUseCase : IInitialSetupUseCase
{
    private readonly IAppMetadataRepository metadata;
    private readonly ISettingSnapshotRepository settings;
    private readonly IClosingRuleRepository closingRules;
    private readonly ITransactionRunner transactions;

    /// <summary>必要な永続化ポートを指定して生成します。</summary>
    public InitialSetupUseCase(IAppMetadataRepository metadata, ISettingSnapshotRepository settings,
        IClosingRuleRepository closingRules, ITransactionRunner transactions)
    {
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.closingRules = closingRules ?? throw new ArgumentNullException(nameof(closingRules));
        this.transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    }

    /// <inheritdoc />
    public async Task<InitialSetupStateDto> GetStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await metadata.GetAsync(cancellationToken).ConfigureAwait(false);
        var issues = await ValidateAsync(value, cancellationToken).ConfigureAwait(false);
        var status = value.InitialSetupStatus == InitialSetupStatus.Completed && issues.Count != 0
            ? InitialSetupStatus.InProgress
            : value.InitialSetupStatus;
        return new(status, value.InitialSetupStep, issues);
    }

    /// <inheritdoc />
    public async Task SaveProgressAsync(string step, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(step)) throw new ArgumentException("初期設定のステップを指定してください。", nameof(step));
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = step.Trim();
        if (normalized.Length > 100) throw new ArgumentOutOfRangeException(nameof(step), "ステップは100文字以内で指定してください。");
        await transactions.ExecuteAsync(async token =>
        {
            var current = await metadata.GetAsync(token).ConfigureAwait(false);
            if (current.InitialSetupStatus == InitialSetupStatus.Completed) return;
            await metadata.SetInitialSetupAsync(InitialSetupStatus.InProgress, normalized, current.InitialSnapshotId, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<InitialSetupStateDto> CompleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await transactions.ExecuteAsync(async token =>
        {
            var current = await metadata.GetAsync(token).ConfigureAwait(false);
            var issues = await ValidateAsync(current, token).ConfigureAwait(false);
            if (issues.Count != 0) return new InitialSetupStateDto(InitialSetupStatus.InProgress, current.InitialSetupStep, issues);
            await metadata.SetInitialSetupAsync(InitialSetupStatus.Completed, null, current.InitialSnapshotId, token).ConfigureAwait(false);
            return new InitialSetupStateDto(InitialSetupStatus.Completed, null, Array.Empty<IssueDto>());
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<IssueDto>> ValidateAsync(AppMetadata value, CancellationToken cancellationToken)
    {
        var issues = new List<IssueDto>();
        if (value.InitialSnapshotId is null)
            issues.Add(ApplicationSupport.Issue("SETUP_SNAPSHOT_REQUIRED", "サービスと単価の設定を完了してください。"));
        else
        {
            var snapshot = await settings.FindAsync(value.InitialSnapshotId.Value, cancellationToken).ConfigureAwait(false);
            if (snapshot is null || !HasApplicableRatesForEnabledServices(snapshot))
                issues.Add(ApplicationSupport.Issue("SETUP_CALCULATION_SETTINGS_REQUIRED", "給与計算に必要なサービスと単価を設定してください。"));
        }
        if ((await closingRules.GetHistoryAsync(cancellationToken).ConfigureAwait(false)).Count == 0)
            issues.Add(ApplicationSupport.Issue("SETUP_CLOSING_RULE_REQUIRED", "給与の締め日を設定してください。"));
        return issues;
    }

    private static bool HasApplicableRatesForEnabledServices(SettingSnapshot snapshot)
    {
        var enabledServices = snapshot.Services.Where(x => x.IsEnabled).ToArray();
        if (enabledServices.Length == 0) return false;
        foreach (var service in enabledServices)
        {
            if (snapshot.Rates.Any(x => x.ServiceId == service.Id && x.TimeCategoryId is null)) continue;
            var enabledCategories = snapshot.TimeCategories.Where(x => x.IsEnabled && x.ServiceId == service.Id).ToArray();
            if (enabledCategories.Length == 0 || enabledCategories.Any(category =>
                    !snapshot.Rates.Any(rate => rate.ServiceId == service.Id && rate.TimeCategoryId == category.Id)))
                return false;
        }
        return true;
    }
}

/// <summary>勤務入力用サービスプリセットを管理します。</summary>
public sealed class ServicePresetUseCase : IServicePresetUseCase
{
    private readonly IServicePresetRepository repository;
    private readonly ITransactionRunner transactions;
    private readonly IAppMetadataRepository metadata;
    private readonly IUtcClock clock;

    /// <summary>必要なポートを指定して生成します。</summary>
    public ServicePresetUseCase(IServicePresetRepository repository, ITransactionRunner transactions,
        IAppMetadataRepository metadata, IUtcClock clock)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServicePresetDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ServicePresetDto> SaveAsync(SaveServicePresetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationSupport.ValidateId(command.ServiceId.Value, nameof(command.ServiceId));
        if (command.TimeCategoryId is { } categoryId) ApplicationSupport.ValidateId(categoryId.Value, nameof(command.TimeCategoryId));
        var name = command.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ApplicationErrorException("PRESET_NAME_REQUIRED", "サービス設定の名前を入力してください。", "DisplayName");
        if (command.DefaultWorkMinutes.Value is < 1 or > 1440) throw new ApplicationErrorException("PRESET_MINUTES_INVALID", "標準勤務時間は1分以上24時間以内で入力してください。", "DefaultWorkMinutes");
        var dto = new ServicePresetDto(command.Id ?? new ServicePresetId(Guid.NewGuid()), name,
            command.ServiceId, command.TimeCategoryId, command.DefaultWorkMinutes, command.DisplayOrder, command.IsEnabled);
        await transactions.ExecuteAsync(async token =>
        {
            await repository.UpsertAsync(dto, token).ConfigureAwait(false);
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return dto;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(ServicePresetId id, CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidateId(id.Value, nameof(id));
        cancellationToken.ThrowIfCancellationRequested();
        await transactions.ExecuteAsync(async token =>
        {
            await repository.DeleteAsync(id, token).ConfigureAwait(false);
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>端末内データのバックアップ案内条件を判定します。</summary>
public sealed class BackupReminderUseCase : IBackupReminderUseCase
{
    private readonly IAppMetadataRepository metadata;
    private readonly IWorkRecordRepository records;
    private readonly ITransactionRunner transactions;
    private readonly ILocalDateConverter localDates;

    /// <summary>必要な読み書きポートを指定して生成します。</summary>
    public BackupReminderUseCase(IAppMetadataRepository metadata, IWorkRecordRepository records,
        ITransactionRunner transactions, ILocalDateConverter localDates)
    {
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.records = records ?? throw new ArgumentNullException(nameof(records));
        this.transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        this.localDates = localDates ?? throw new ArgumentNullException(nameof(localDates));
    }

    /// <inheritdoc />
    public async Task<BackupReminderStateDto> GetStateAsync(DateOnly localToday, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await metadata.GetAsync(cancellationToken).ConfigureAwait(false);
        var hasRecords = await records.AnyAsync(cancellationToken).ConfigureAwait(false);
        var deferred = state.BackupReminderDeferredUntilDate is { } date && localToday < date;
        var oldChangedData = state.LastExportedAtUtc is { } exported && state.LastDataChangedAtUtc is { } changed &&
            changed > exported && localToday.DayNumber - localDates.ToLocalDate(changed).DayNumber >= 30;
        var show = hasRecords && !deferred && (state.LastExportedAtUtc is null || oldChangedData);
        return new(localToday, show, hasRecords, state.LastExportedAtUtc, state.LastDataChangedAtUtc, state.BackupReminderDeferredUntilDate);
    }

    /// <inheritdoc />
    public async Task<BackupReminderStateDto> DeferForSevenDaysAsync(DateOnly localToday, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await transactions.ExecuteAsync(
            token => metadata.SetBackupReminderDeferredUntilDateAsync(localToday.AddDays(7), token),
            cancellationToken).ConfigureAwait(false);
        return await GetStateAsync(localToday, cancellationToken).ConfigureAwait(false);
    }
}
