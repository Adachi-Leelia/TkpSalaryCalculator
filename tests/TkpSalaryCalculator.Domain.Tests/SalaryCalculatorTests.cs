using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Tests;

public sealed class SalaryCalculatorTests
{
    private readonly SalaryCalculator calculator = new();

    [Fact(DisplayName = "CALC-001 固定額方式は勤務分数で按分しない")]
    public void Calc001_FixedRate()
    {
        var result = Calculate(rate: TestData.Rate(RateType.FixedPerRecord, 850), minutes: 30);

        Assert.Equal(850, result.BasePay!.Value.Value);
        Assert.Equal(850, result.Total!.Value.Value);
    }

    [Fact(DisplayName = "CALC-002 時給1200円の30分は600円")]
    public void Calc002_HourlyRate()
    {
        var result = Calculate(rate: TestData.Rate(RateType.Hourly, 1200), minutes: 30);

        Assert.Equal(600, result.BasePay!.Value.Value);
    }

    [Fact(DisplayName = "CALC-003 時給の1円未満は明細算出時に切り上げる")]
    public void Calc003_HourlyRateRoundsUp()
    {
        var result = Calculate(rate: TestData.Rate(RateType.Hourly, 1001), minutes: 30);

        Assert.Equal(501, result.BasePay!.Value.Value);
    }

    [Fact(DisplayName = "CALC-004 基本給与の整数円へ割合割増を適用して切り上げる")]
    public void Calc004_PercentagePremium()
    {
        var premium = TestData.PercentagePremium(2500);
        var result = Calculate(TestData.Rate(RateType.Hourly, 1001), 30, [premium]);

        Assert.Equal(126, Assert.Single(result.Premiums).Amount.Value);
        Assert.Equal(627, result.Total!.Value.Value);
    }

    [Fact(DisplayName = "CALC-005 時間当たり固定額割増を対象分数で切り上げる")]
    public void Calc005_FixedPerHourPremium()
    {
        var premium = TestData.FixedPerHourPremium(301);
        var result = Calculate(TestData.Rate(RateType.FixedPerRecord, 0), 30, [premium]);

        Assert.Equal(151, Assert.Single(result.Premiums).Amount.Value);
    }

    [Fact(DisplayName = "CALC-006 1件固定額割増を1回加算する")]
    public void Calc006_FixedPerRecordPremium()
    {
        var premium = TestData.FixedPerRecordPremium(200);
        var result = Calculate(TestData.Rate(RateType.FixedPerRecord, 0), 30, [premium]);

        Assert.Equal(200, Assert.Single(result.Premiums).Amount.Value);
    }

    [Fact(DisplayName = "CALC-007 基本給、休日、夜間、件数加算を合計する")]
    public void Calc007_AllComponents()
    {
        var holiday = TestData.PercentagePremium(2500, weekdays: [DayOfWeek.Saturday]);
        var night = TestData.FixedPerHourPremium(200, start: 22 * 60, end: 5 * 60);
        var bonus = TestData.CountBonus(150);
        var result = Calculate(
            TestData.Rate(RateType.Hourly, 1200),
            60,
            [holiday, night],
            [bonus],
            date: new DateOnly(2026, 8, 15),
            start: 21 * 60 + 30);

        Assert.Equal([300, 100], result.Premiums.Select(item => item.Amount.Value));
        Assert.Equal(150, Assert.Single(result.CountBonuses).Amount.Value);
        Assert.Equal(1750, result.Total!.Value.Value);
    }

    [Fact(DisplayName = "CALC-008 異なる複数割増をすべて適用する")]
    public void Calc008_MultiplePremiums()
    {
        var result = Calculate(
            TestData.Rate(RateType.FixedPerRecord, 1000),
            60,
            [TestData.FixedPerRecordPremium(100), TestData.FixedPerRecordPremium(200)]);

        Assert.Equal([100, 200], result.Premiums.Select(item => item.Amount.Value));
        Assert.Equal(1300, result.Total!.Value.Value);
    }

    [Fact(DisplayName = "CALC-009 件数加算は勤務記録ごとに1回、日合計へ全件分加算する")]
    public void Calc009_CountBonusPerRecord()
    {
        var bonus = TestData.CountBonus(150);
        var first = Calculate(TestData.Rate(RateType.FixedPerRecord, 0), 30, bonuses: [bonus]);
        var second = Calculate(TestData.Rate(RateType.FixedPerRecord, 0), 30, bonuses: [bonus], recordId: Guid.NewGuid());

        var day = calculator.AggregateDay(new DateOnly(2026, 8, 15), [first, second]);

        Assert.Equal(300, day.CountBonusSubtotal.Value);
        Assert.Equal(300, day.CalculatedSubtotal.Value);
    }

    [Fact(DisplayName = "CALC-010 月額手当は給与期間へ1回だけ加算する")]
    public void Calc010_MonthlyAllowance()
    {
        var period = TestData.Period(2026, 8, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 20));
        var first = TestData.CalculatedRecord(1750);
        var second = TestData.CalculatedRecord(2000, Guid.NewGuid());
        var days = new[]
        {
            calculator.AggregateDay(new DateOnly(2026, 8, 1), [first]),
            calculator.AggregateDay(new DateOnly(2026, 8, 2), [second]),
        };
        var allowance = new MonthlyAllowance(
            new MonthlyAllowanceId(Guid.NewGuid()), period.Key, "月額手当", new YenAmount(5000));

        var result = calculator.AggregatePeriod(period, days, [allowance]);

        Assert.Equal(5000, result.AllowanceSubtotal.Value);
        Assert.Equal(8750, result.CalculatedSubtotal.Value);
    }

    [Fact(DisplayName = "CALC-011 同じ入力は同じ内訳と合計を返す")]
    public void Calc011_Deterministic()
    {
        var request = TestData.Request(
            TestData.WorkRecord(60, start: 22 * 60),
            TestData.Snapshot(
                TestData.Rate(RateType.Hourly, 1200),
                premiums: [TestData.FixedPerHourPremium(200, 22 * 60, 5 * 60)],
                bonuses: [TestData.CountBonus(150)]));

        var first = calculator.Calculate(request);
        var second = calculator.Calculate(request);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.BasePay, second.BasePay);
        Assert.Equal(first.Total, second.Total);
        Assert.Equal(first.Premiums.Select(ToPremiumTuple), second.Premiums.Select(ToPremiumTuple));
        Assert.Equal(first.CountBonuses.Select(ToBonusTuple), second.CountBonuses.Select(ToBonusTuple));
    }

    [Fact(DisplayName = "CALC-012 基本単価がなければ推測せず未計算を返す")]
    public void Calc012_MissingRate()
    {
        var snapshot = TestData.Snapshot(rate: null);

        var result = calculator.Calculate(TestData.Request(TestData.WorkRecord(30), snapshot));

        Assert.Equal(SalaryCalculationStatus.Uncalculated, result.Status);
        Assert.Null(Assert.Single(result.TaskCalculations).AppliedRate);
        Assert.Null(result.BasePay);
        Assert.Null(result.Total);
        Assert.Contains(result.MissingRequirements, item => item.Code == MissingCalculationRequirementCodes.Rate);
    }

    [Fact(DisplayName = "CALC-020 時間帯と重なる30分だけ割増対象にする")]
    public void Calc020_PartialNightOverlap()
    {
        var night = TestData.FixedPerHourPremium(300, 22 * 60, 5 * 60);
        var result = Calculate(TestData.Rate(RateType.FixedPerRecord, 0), 60, [night], start: 21 * 60 + 30);

        var applied = Assert.Single(result.Premiums);
        Assert.Equal(30, applied.ApplicableMinutes.Value);
        Assert.Equal(150, applied.Amount.Value);
    }

    [Fact(DisplayName = "CALC-021 日付をまたぐ勤務は開始日の1件60分として扱う")]
    public void Calc021_CrossMidnightWork()
    {
        var record = TestData.WorkRecord(60, date: new DateOnly(2026, 8, 15), start: 23 * 60 + 30);

        Assert.Equal(new DateOnly(2026, 8, 15), record.WorkDate);
        Assert.Equal(60, Assert.Single(record.Tasks).WorkMinutes.Value);
        Assert.Equal(30, Assert.Single(record.Tasks).EndTime!.Value.Value);
    }

    [Fact(DisplayName = "CALC-022 終了時刻が開始時刻以前なら翌日として時間差を検証する")]
    public void Calc022_EndAtNextDay()
    {
        var record = TestData.CreateWorkRecord(
            new WorkRecordId(Guid.NewGuid()),
            new DateOnly(2026, 8, 15),
            TestData.ServiceId,
            null,
            WorkInputMode.TimeRange,
            new WorkMinutes(60),
            new MinuteOfDay(23 * 60 + 30),
            new MinuteOfDay(30));

        Assert.Equal(60, Assert.Single(record.Tasks).WorkMinutes.Value);
    }

    [Fact(DisplayName = "CALC-023 日付またぎ勤務は開始日が属する給与期間へ含める")]
    public void Calc023_StartDateDeterminesPeriod()
    {
        var periods = new PayrollPeriodCalculator();
        var rules = new[] { TestData.ClosingRule(2020, 1, 20) };
        var record = TestData.CreateWorkRecord(
            new WorkRecordId(Guid.NewGuid()),
            new DateOnly(2026, 8, 20),
            TestData.ServiceId,
            null,
            WorkInputMode.TimeRange,
            new WorkMinutes(60),
            new MinuteOfDay(23 * 60 + 30),
            new MinuteOfDay(30));

        var period = periods.FindPeriod(record.WorkDate, rules);
        var nextDatePeriod = periods.FindPeriod(record.WorkDate.AddDays(1), rules);

        Assert.Equal(new YearMonth(2026, 8), period.Key.Value);
        Assert.Equal(new DateOnly(2026, 8, 20), period.EndDate);
        Assert.Equal(new YearMonth(2026, 9), nextDatePeriod.Key.Value);
        Assert.Equal(60, Assert.Single(record.Tasks).WorkMinutes.Value);
    }

    [Fact(DisplayName = "CALC-024 同じルールの曜日と祝日の一致を重複加算しない")]
    public void Calc024_DateConditionOrWithoutDuplication()
    {
        var holiday = TestData.FixedPerRecordPremium(
            200,
            weekdays: [DayOfWeek.Saturday],
            usesNationalHolidays: true);
        var result = Calculate(
            TestData.Rate(RateType.FixedPerRecord, 0),
            60,
            [holiday],
            date: new DateOnly(2026, 8, 15),
            holidays: [new DateOnly(2026, 8, 15)]);

        Assert.Equal(200, Assert.Single(result.Premiums).Amount.Value);
    }

    [Fact(DisplayName = "CALC-025 異なる休日割増と夜間割増を両方適用する")]
    public void Calc025_HolidayAndNightPremiums()
    {
        var holiday = TestData.FixedPerRecordPremium(100, weekdays: [DayOfWeek.Saturday]);
        var night = TestData.FixedPerRecordPremium(200, start: 22 * 60, end: 5 * 60);
        var result = Calculate(
            TestData.Rate(RateType.FixedPerRecord, 0),
            60,
            [holiday, night],
            date: new DateOnly(2026, 8, 15),
            start: 23 * 60 + 30);

        Assert.Equal([100, 200], result.Premiums.Select(item => item.Amount.Value));
    }

    [Fact(DisplayName = "CALC-030 固定基本給を対象時間で按分してから割合を切り上げる")]
    public void Calc030_PartialPercentageOfFixedRate()
    {
        var premium = TestData.PercentagePremium(2500, start: 22 * 60, end: 22 * 60 + 15);
        var result = Calculate(TestData.Rate(RateType.FixedPerRecord, 850), 30, [premium], start: 22 * 60);

        Assert.Equal(107, Assert.Single(result.Premiums).Amount.Value);
    }

    [Fact(DisplayName = "CALC-031 1件固定割増は1分の重なりでも按分しない")]
    public void Calc031_OneMinuteFixedPremium()
    {
        var premium = TestData.FixedPerRecordPremium(200, start: 22 * 60, end: 22 * 60 + 1);
        var result = Calculate(TestData.Rate(RateType.FixedPerRecord, 0), 30, [premium], start: 22 * 60);

        Assert.Equal(1, Assert.Single(result.Premiums).ApplicableMinutes.Value);
        Assert.Equal(200, result.Premiums[0].Amount.Value);
    }

    [Fact(DisplayName = "CALC-032 任意時間はサービス単位単価だけを使用する")]
    public void Calc032_DurationUsesServiceRate()
    {
        var snapshot = TestData.Snapshot(
            TestData.Rate(RateType.Hourly, 1200),
            additionalRates: [TestData.CategoryRate(RateType.FixedPerRecord, 9999)]);
        var result = calculator.Calculate(TestData.Request(TestData.WorkRecord(45, timeCategory: null), snapshot));

        Assert.Null(Assert.Single(result.TaskCalculations).AppliedRate!.TimeCategoryId);
        Assert.Equal(900, result.BasePay!.Value.Value);
    }

    [Fact(DisplayName = "CALC-033 時間区分単価をサービス単位単価より優先する")]
    public void Calc033_CategoryRateTakesPriority()
    {
        var snapshot = TestData.Snapshot(
            TestData.Rate(RateType.Hourly, 1200),
            additionalRates: [TestData.CategoryRate(RateType.FixedPerRecord, 850)]);
        var result = calculator.Calculate(TestData.Request(TestData.WorkRecord(30, timeCategory: TestData.CategoryId), snapshot));

        Assert.Equal(TestData.CategoryId, Assert.Single(result.TaskCalculations).AppliedRate!.TimeCategoryId);
        Assert.Equal(850, result.BasePay!.Value.Value);
    }

    [Fact(DisplayName = "CALC-034 勤務時間入力の終了時刻を開始時刻と分数から導出する")]
    public void Calc034_DurationDerivesEndTime()
    {
        var record = TestData.WorkRecord(120, start: 23 * 60 + 30);

        Assert.Equal(90, Assert.Single(record.Tasks).EndTime!.Value.Value);
    }

    [Fact(DisplayName = "CALC-035 日付をまたぐ割増時間帯との重なりを求める")]
    public void Calc035_CrossMidnightPremiumWindow()
    {
        var premium = TestData.FixedPerHourPremium(60, 22 * 60, 5 * 60);
        var result = Calculate(TestData.Rate(RateType.FixedPerRecord, 0), 60, [premium], start: 23 * 60 + 30);

        Assert.Equal(60, Assert.Single(result.Premiums).ApplicableMinutes.Value);
        Assert.Equal(60, result.Premiums[0].Amount.Value);
    }

    [Fact(DisplayName = "CALC-036 日付条件は日付またぎ後も勤務開始日だけで判定する")]
    public void Calc036_DateConditionUsesStartDate()
    {
        var sunday = TestData.FixedPerHourPremium(60, weekdays: [DayOfWeek.Sunday]);
        var result = Calculate(
            TestData.Rate(RateType.FixedPerRecord, 0),
            60,
            [sunday],
            date: new DateOnly(2026, 8, 16),
            start: 23 * 60 + 30);

        Assert.Equal(60, Assert.Single(result.Premiums).ApplicableMinutes.Value);
    }

    [Fact(DisplayName = "CALC-037 対象基本給を整数円へ切り上げてから割合を適用する")]
    public void Calc037_RoundingOrder()
    {
        var premium = TestData.PercentagePremium(2500);
        var result = Calculate(TestData.Rate(RateType.Hourly, 1001), 30, [premium]);

        Assert.Equal(501, result.BasePay!.Value.Value);
        Assert.Equal(126, Assert.Single(result.Premiums).Amount.Value);
    }

    private WorkSalaryCalculation Calculate(
        SnapshotRate rate,
        int minutes,
        IReadOnlyList<SnapshotPremium>? premiums = null,
        IReadOnlyList<SnapshotCountBonus>? bonuses = null,
        DateOnly? date = null,
        int? start = null,
        IEnumerable<DateOnly>? holidays = null,
        Guid? recordId = null)
    {
        var snapshot = TestData.Snapshot(rate, premiums, bonuses);
        var record = TestData.WorkRecord(minutes, date, start, recordId: recordId);
        return calculator.Calculate(TestData.Request(record, snapshot, holidays));
    }

    private static (PremiumId Id, int Minutes, long Amount) ToPremiumTuple(AppliedPremium item)
    {
        return (item.Rule.Id, item.ApplicableMinutes.Value, item.Amount.Value);
    }


    private static (CountBonusId Id, long Amount) ToBonusTuple(AppliedCountBonus item)
    {
        return (item.CountBonusId, item.Amount.Value);
    }

}
