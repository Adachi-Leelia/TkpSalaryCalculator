using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Internal;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.UseCases;

/// <summary>締め日履歴と給与期間へ直接関連付ける月額手当を管理します。</summary>
/// <remarks>必要なポートを指定して生成します。</remarks>
public sealed class PayrollPeriodSettingsUseCase(IClosingRuleRepository closingRules,
    IMonthlyAllowanceRepository allowances, ITransactionRunner transactions,
    IAppMetadataRepository metadata, IUtcClock clock, IPayrollPeriodCalculator periodCalculator) : IPayrollPeriodSettingsUseCase
{
    private readonly IClosingRuleRepository closingRules = closingRules ?? throw new ArgumentNullException(nameof(closingRules));
    private readonly IMonthlyAllowanceRepository allowances = allowances ?? throw new ArgumentNullException(nameof(allowances));
    private readonly ITransactionRunner transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    private readonly IAppMetadataRepository metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IPayrollPeriodCalculator periodCalculator = periodCalculator ?? throw new ArgumentNullException(nameof(periodCalculator));

    /// <inheritdoc />
    public async Task<PayrollPeriod> FindPeriodAsync(DateOnly localDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var history = ClosingRuleHistorySupport.ForCalculation(
            await closingRules.GetHistoryAsync(cancellationToken).ConfigureAwait(false));
        if (history.Count == 0)
        {
            throw new ApplicationErrorException(
                "CLOSING_RULE_REQUIRED",
                "給与算定期間を決定するため、締め日を設定してください。");
        }

        return periodCalculator.FindPeriod(localDate, history);
    }

    /// <inheritdoc />
    public async Task<ClosingRuleReplacementPreviewDto> PreviewClosingRuleReplacementAsync(
        ReplaceClosingRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ApplicationSupport.ValidatePayrollPeriodKey(command.EffectiveFrom, nameof(command.EffectiveFrom));
        cancellationToken.ThrowIfCancellationRequested();
        if (command.ClosingDay is < 1 or > 31)
            throw new ApplicationErrorException("CLOSING_DAY_INVALID", "締め日は1日から31日、または月末を選択してください。", "ClosingDay");
        var historySnapshot = await closingRules.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var history = ClosingRuleHistorySupport.ForCalculation(historySnapshot.Rules);
        PayrollPeriod? current = null;
        try { current = periodCalculator.GetPeriod(command.EffectiveFrom, history); }
        catch (ArgumentException) { }
        var existing = history.FirstOrDefault(x => x.EffectiveFrom == command.EffectiveFrom);
        var replacementRule = new ClosingRule(existing?.Id ?? new ClosingRuleId(Guid.NewGuid()),
            command.EffectiveFrom, command.ClosingDay);
        var replacementHistory = ClosingRuleHistorySupport.WithReplacementForCalculation(historySnapshot.Rules, replacementRule);
        var replacement = periodCalculator.GetPeriod(command.EffectiveFrom, replacementHistory);
        return new(command.EffectiveFrom, current, replacement,
            new ClosingRuleReplacementConfirmationToken(command.EffectiveFrom, command.ClosingDay, historySnapshot.Version));
    }

    /// <inheritdoc />
    public async Task<EffectiveClosingRuleDto?> GetClosingRuleAsync(PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidatePayrollPeriodKey(payrollPeriodKey, nameof(payrollPeriodKey));
        cancellationToken.ThrowIfCancellationRequested();
        var rule = (await closingRules.GetHistoryAsync(cancellationToken).ConfigureAwait(false))
            .Where(x => x.EffectiveFrom.Value.CompareTo(payrollPeriodKey.Value) <= 0)
            .OrderBy(x => x.EffectiveFrom.Value).LastOrDefault();
        return rule is null ? null : new(payrollPeriodKey, rule.Id, rule.EffectiveFrom, rule.ClosingDay);
    }

    /// <inheritdoc />
    public async Task ReplaceClosingRuleAsync(ReplaceClosingRuleCommand command,
        ClosingRuleReplacementConfirmationToken confirmationToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(confirmationToken);
        ApplicationSupport.ValidatePayrollPeriodKey(command.EffectiveFrom, nameof(command.EffectiveFrom));
        cancellationToken.ThrowIfCancellationRequested();
        if (command.ClosingDay is < 1 or > 31)
            throw new ApplicationErrorException("CLOSING_DAY_INVALID", "締め日は1日から31日、または月末を選択してください。", "ClosingDay");
        if (confirmationToken.EffectiveFrom != command.EffectiveFrom || confirmationToken.ClosingDay != command.ClosingDay)
            throw ClosingHistoryChanged();
        await transactions.ExecuteAsync(async token =>
        {
            var currentHistory = await closingRules.GetHistoryAsync(token).ConfigureAwait(false);
            var existing = currentHistory
                .FirstOrDefault(x => x.EffectiveFrom == command.EffectiveFrom);
            var effectiveFrom = currentHistory.Count == 0 ? ClosingRuleHistorySupport.Baseline : command.EffectiveFrom;
            var rule = new ClosingRule(existing?.Id ?? new ClosingRuleId(Guid.NewGuid()), effectiveFrom, command.ClosingDay);
            if (!await closingRules.TryReplaceEffectiveRuleAsync(rule, confirmationToken.HistoryVersion, token).ConfigureAwait(false))
                throw ClosingHistoryChanged();
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MonthlyAllowanceDto>> GetAllowancesAsync(PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidatePayrollPeriodKey(payrollPeriodKey, nameof(payrollPeriodKey));
        cancellationToken.ThrowIfCancellationRequested();
        return [.. (await allowances.GetForPeriodAsync(payrollPeriodKey, cancellationToken).ConfigureAwait(false)).Select(x => new MonthlyAllowanceDto(x.Id, x.DisplayName, x.Amount))];
    }

    /// <inheritdoc />
    public async Task<MonthlyAllowanceDto> SaveAllowanceAsync(SaveMonthlyAllowanceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ApplicationSupport.ValidatePayrollPeriodKey(command.PayrollPeriodKey, nameof(command.PayrollPeriodKey));
        cancellationToken.ThrowIfCancellationRequested();
        var name = command.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ApplicationErrorException("ALLOWANCE_NAME_REQUIRED", "月額手当の名前を入力してください。", "DisplayName");
        if (command.Amount.Value < 0)
            throw new ApplicationErrorException("ALLOWANCE_AMOUNT_NEGATIVE", "月額手当は0円以上で入力してください。", "Amount");
        var value = new MonthlyAllowance(command.Id ?? new MonthlyAllowanceId(Guid.NewGuid()),
            command.PayrollPeriodKey, name, command.Amount);
        await transactions.ExecuteAsync(async token =>
        {
            await allowances.UpsertAsync(value, token).ConfigureAwait(false);
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return new(value.Id, value.DisplayName, value.Amount);
    }

    /// <inheritdoc />
    public async Task DeleteAllowanceAsync(MonthlyAllowanceId id, CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidateId(id.Value, nameof(id));
        cancellationToken.ThrowIfCancellationRequested();
        await transactions.ExecuteAsync(async token =>
        {
            await allowances.DeleteAsync(id, token).ConfigureAwait(false);
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ApplicationErrorException ClosingHistoryChanged()
    {
        return new(
        "CLOSING_RULE_PREVIEW_STALE", "確認後に締め日履歴が変更されました。給与期間をもう一度確認してください。");
    }

}
