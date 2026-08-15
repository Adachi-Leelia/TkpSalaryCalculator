using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Tests;

public sealed class PayrollPeriodHistoryReviewTests
{
    private readonly PayrollPeriodCalculator calculator = new();

    [Fact]
    public void UnsortedHistoryUsesLatestApplicableRule()
    {
        var rules = new[]
        {
            TestData.ClosingRule(2027, 1, 5),
            TestData.ClosingRule(2020, 1, 20),
            TestData.ClosingRule(2026, 8, 15),
            TestData.ClosingRule(2025, 4, 25),
        };

        var period = GetPeriod(2026, 9, rules);

        Assert.Equal(new DateOnly(2026, 8, 16), period.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 15), period.EndDate);
    }

    [Fact]
    public void FindPeriodAfterClosingDaySelectsFollowingPeriod()
    {
        var rules = new[] { TestData.ClosingRule(2020, 1, 20) };

        var period = calculator.FindPeriod(new DateOnly(2026, 8, 21), rules);

        Assert.Equal(new YearMonth(2026, 9), period.Key.Value);
        Assert.Equal(new DateOnly(2026, 8, 21), period.StartDate);
    }

    [Fact]
    public void MovingClosingDayLaterKeepsContinuousBoundary()
    {
        var rules = new[]
        {
            TestData.ClosingRule(2020, 1, 15),
            TestData.ClosingRule(2026, 8, 20),
        };
        var previous = GetPeriod(2026, 7, rules);
        var changed = GetPeriod(2026, 8, rules);

        Assert.Equal(previous.EndDate.AddDays(1), changed.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 20), changed.EndDate);
    }

    [Fact]
    public void ChangingToEndOfMonthKeepsContinuousBoundary()
    {
        var rules = new[]
        {
            TestData.ClosingRule(2020, 1, 20),
            TestData.ClosingRule(2026, 8, null),
        };
        var previous = GetPeriod(2026, 7, rules);
        var changed = GetPeriod(2026, 8, rules);

        Assert.Equal(previous.EndDate.AddDays(1), changed.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 31), changed.EndDate);
    }

    [Fact]
    public void DuplicateRuleIdIsRejected()
    {
        var duplicateId = new ClosingRuleId(Guid.NewGuid());
        var rules = new[]
        {
            new ClosingRule(duplicateId, Key(2020, 1), 20),
            new ClosingRule(duplicateId, Key(2026, 8), 15),
        };

        Assert.Throws<ArgumentException>(() => GetPeriod(2026, 9, rules));
    }

    [Fact]
    public void NullHistoryIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => calculator.GetPeriod(Key(2026, 8), null!));
        Assert.Throws<ArgumentNullException>(() => calculator.FindPeriod(new DateOnly(2026, 8, 1), null!));
    }

    private PayrollPeriod GetPeriod(int year, int month, IReadOnlyList<ClosingRule> rules)
    {
        return calculator.GetPeriod(Key(year, month), rules);
    }


    private static PayrollPeriodKey Key(int year, int month)
    {
        return new(new YearMonth(year, month));
    }

}
