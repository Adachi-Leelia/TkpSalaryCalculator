using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Domain.ValueObjects;
using TkpSalaryCalculator.Domain.Models;

namespace TkpSalaryCalculator.Domain.Tests;

public sealed class PayrollPeriodCalculatorTests
{
    private readonly PayrollPeriodCalculator calculator = new();

    [Fact(DisplayName = "PERIOD-001 20日締めは前月21日から当月20日")]
    public void Period001_ClosingDay20()
    {
        var period = GetPeriod(2026, 8, new[] { TestData.ClosingRule(2020, 1, 20) });

        Assert.Equal(new DateOnly(2026, 7, 21), period.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 20), period.EndDate);
    }

    [Fact(DisplayName = "PERIOD-002 平年2月の月末締め")]
    public void Period002_EndOfFebruary()
    {
        var period = GetPeriod(2026, 2, new[] { TestData.ClosingRule(2020, 1, null) });

        Assert.Equal(new DateOnly(2026, 2, 1), period.StartDate);
        Assert.Equal(new DateOnly(2026, 2, 28), period.EndDate);
    }

    [Fact(DisplayName = "PERIOD-003 うるう年2月の月末締め")]
    public void Period003_LeapYear()
    {
        var period = GetPeriod(2024, 2, new[] { TestData.ClosingRule(2020, 1, null) });

        Assert.Equal(new DateOnly(2024, 2, 29), period.EndDate);
    }

    [Fact(DisplayName = "PERIOD-004 31日がない月は月末を終了日とする")]
    public void Period004_ClosingDay31InApril()
    {
        var period = GetPeriod(2026, 4, new[] { TestData.ClosingRule(2020, 1, 31) });

        Assert.Equal(new DateOnly(2026, 4, 30), period.EndDate);
    }

    [Fact(DisplayName = "PERIOD-005 30日がない2月は月末を終了日とする")]
    public void Period005_ClosingDay30InFebruary()
    {
        var period = GetPeriod(2026, 2, new[] { TestData.ClosingRule(2020, 1, 30) });

        Assert.Equal(new DateOnly(2026, 2, 28), period.EndDate);
    }

    [Fact(DisplayName = "PERIOD-006 締め日変更月の開始日は旧ルールによる前期間終了日の翌日")]
    public void Period006_ClosingDayHistory()
    {
        var rules = new[]
        {
            TestData.ClosingRule(2020, 1, 20),
            TestData.ClosingRule(2026, 8, 15),
        };

        var july = GetPeriod(2026, 7, rules);
        var august = GetPeriod(2026, 8, rules);

        Assert.Equal(new DateOnly(2026, 7, 20), july.EndDate);
        Assert.Equal(july.EndDate.AddDays(1), august.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 15), august.EndDate);
    }

    [Fact(DisplayName = "PERIOD-007 年をまたぐ期間は両端に異なる年を保持する")]
    public void Period007_CrossYear()
    {
        var period = GetPeriod(2026, 1, new[] { TestData.ClosingRule(2020, 1, 20) });

        Assert.Equal(new DateOnly(2025, 12, 21), period.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 20), period.EndDate);
    }

    [Fact(DisplayName = "PERIOD-008 期間の開始日と終了日を両方含む")]
    public void Period008_InclusiveBoundaries()
    {
        var rules = new[] { TestData.ClosingRule(2020, 1, 20) };
        var period = GetPeriod(2026, 8, rules);

        Assert.Equal(period.Key, calculator.FindPeriod(period.StartDate, rules).Key);
        Assert.Equal(period.Key, calculator.FindPeriod(period.EndDate, rules).Key);
    }

    [Fact(DisplayName = "給与期間は複数年にわたり空白も重複もない")]
    public void Periods_AreContinuousAcrossMultipleYears()
    {
        var rules = new[]
        {
            TestData.ClosingRule(2019, 1, 20),
            TestData.ClosingRule(2021, 4, null),
            TestData.ClosingRule(2022, 8, 31),
            TestData.ClosingRule(2023, 2, 15),
        };
        var key = new YearMonth(2020, 1);
        var previous = GetPeriod(key.Year, key.Month, rules);

        for (var index = 1; index < 60; index++)
        {
            key = key.AddMonths(1);
            var current = GetPeriod(key.Year, key.Month, rules);
            Assert.Equal(previous.EndDate.AddDays(1), current.StartDate);
            previous = current;
        }
    }

    [Fact(DisplayName = "同じ適用開始年月の締め日履歴を拒否する")]
    public void DuplicateEffectiveMonth_IsRejected()
    {
        var rules = new[]
        {
            TestData.ClosingRule(2020, 1, 20),
            TestData.ClosingRule(2020, 1, 15),
        };

        Assert.Throws<ArgumentException>(() => GetPeriod(2026, 8, rules));
    }

    [Fact(DisplayName = "対象月または前月へ適用可能な締め日履歴がなければ拒否する")]
    public void MissingClosingRule_IsRejected()
    {
        var rules = new[] { TestData.ClosingRule(2026, 8, 20) };

        Assert.Throws<ArgumentException>(() => GetPeriod(2026, 8, rules));
    }

    private PayrollPeriod GetPeriod(int year, int month, IReadOnlyList<ClosingRule> rules) =>
        calculator.GetPeriod(new PayrollPeriodKey(new YearMonth(year, month)), rules);
}
