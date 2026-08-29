using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Internal;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.UseCases;

/// <summary>年間給与見込み累計の締め月を取得および保存します。</summary>
public sealed class AnnualSummarySettingsUseCase(
    IAnnualSummarySettingRepository settings,
    ITransactionRunner transactions,
    IAppMetadataRepository metadata,
    IUtcClock clock) : IAnnualSummarySettingsUseCase
{
    private readonly IAnnualSummarySettingRepository settings =
        settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ITransactionRunner transactions =
        transactions ?? throw new ArgumentNullException(nameof(transactions));
    private readonly IAppMetadataRepository metadata =
        metadata ?? throw new ArgumentNullException(nameof(metadata));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public async Task<AnnualSummarySettingDto> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new AnnualSummarySettingDto(
            await settings.GetClosingMonthAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<AnnualSummarySettingDto> SaveAsync(
        SaveAnnualSummarySettingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        AnnualClosingMonth closingMonth;
        try
        {
            closingMonth = new AnnualClosingMonth(command.ClosingMonth);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new ApplicationErrorException(
                "ANNUAL_CLOSING_MONTH_INVALID",
                "年間締め月は1月から12月までで選択してください。",
                "ClosingMonth",
                exception);
        }

        await transactions.ExecuteAsync(async token =>
        {
            await settings.SaveClosingMonthAsync(closingMonth, token).ConfigureAwait(false);
            await ApplicationSupport.MarkChangedAsync(metadata, clock, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return new AnnualSummarySettingDto(closingMonth);
    }
}
