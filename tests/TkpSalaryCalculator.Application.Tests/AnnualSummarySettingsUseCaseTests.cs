using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Tests;

public sealed class AnnualSummarySettingsUseCaseTests
{
    [Fact]
    public async Task SavePersistsClosingMonthAndMarksBackupDataChangedInOneTransaction()
    {
        var repository = new FakeAnnualSummarySettingRepository();
        var metadata = new FakeMetadataRepository();
        var transactions = new FakeTransactionRunner();
        transactions.Register(repository, metadata);
        var now = new DateTimeOffset(2026, 8, 29, 3, 0, 0, TimeSpan.Zero);
        var useCase = new AnnualSummarySettingsUseCase(
            repository, transactions, metadata, new FakeClock(now));

        var result = await useCase.SaveAsync(new SaveAnnualSummarySettingCommand(3), default);

        Assert.Equal(3, result.ClosingMonth.Value);
        Assert.Equal(3, repository.Value.Value);
        Assert.Equal(1, repository.SaveCalls);
        Assert.Equal(now, metadata.Value.LastDataChangedAtUtc);
        Assert.Equal(1, transactions.Commits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task InvalidClosingMonthDoesNotWrite(int value)
    {
        var repository = new FakeAnnualSummarySettingRepository();
        var metadata = new FakeMetadataRepository();
        var transactions = new FakeTransactionRunner();
        var useCase = new AnnualSummarySettingsUseCase(
            repository, transactions, metadata, new FakeClock(DateTimeOffset.UnixEpoch));

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            useCase.SaveAsync(new SaveAnnualSummarySettingCommand(value), default));

        Assert.Equal("ANNUAL_CLOSING_MONTH_INVALID", exception.Code);
        Assert.Equal(0, repository.SaveCalls);
        Assert.Equal(0, transactions.Calls);
    }

    [Fact]
    public async Task MetadataFailureRollsBackClosingMonth()
    {
        var repository = new FakeAnnualSummarySettingRepository();
        var metadata = new FakeMetadataRepository
        {
            SetLastDataChangedFailure = new InvalidOperationException("metadata failure"),
        };
        var transactions = new FakeTransactionRunner();
        transactions.Register(repository, metadata);
        var useCase = new AnnualSummarySettingsUseCase(
            repository, transactions, metadata, new FakeClock(DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.SaveAsync(new SaveAnnualSummarySettingCommand(3), default));

        Assert.Equal(12, repository.Value.Value);
        Assert.Equal(0, transactions.Commits);
    }
}
