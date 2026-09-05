using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Tests;

internal static class TestData
{
    public static readonly ServiceId ServiceId = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));
    public static readonly TimeCategoryId CategoryId = new(Guid.Parse("20000000-0000-0000-0000-000000000001"));
    public static readonly HolidayCalendarVersionId HolidayVersionId = new(Guid.Parse("30000000-0000-0000-0000-000000000001"));

    public static WorkRecord WorkRecord(
        int minutes,
        DateOnly? date = null,
        int? start = null,
        TimeCategoryId? timeCategory = null,
        Guid? recordId = null)
    {
        var id = new WorkRecordId(recordId ?? Guid.Parse("40000000-0000-0000-0000-000000000001"));
        return new WorkRecord(id, date ?? new DateOnly(2026, 8, 15),
        [
            new WorkTask(new WorkTaskId(id.Value), ServiceId, timeCategory, WorkInputMode.Duration,
                new WorkMinutes(minutes), start is null ? null : new MinuteOfDay(start.Value), null,
                new DisplayOrder(0)),
        ]);
    }

    public static WorkRecord CreateWorkRecord(
        WorkRecordId id,
        DateOnly date,
        ServiceId serviceId,
        TimeCategoryId? timeCategoryId,
        WorkInputMode inputMode,
        WorkMinutes workMinutes,
        MinuteOfDay? startTime,
        MinuteOfDay? endTime) =>
        new(id, date,
        [
            new WorkTask(new WorkTaskId(id.Value), serviceId, timeCategoryId, inputMode,
                workMinutes, startTime, endTime, new DisplayOrder(0)),
        ]);

    public static SnapshotRate Rate(RateType type, long amount)
    {
        return new(ServiceId, null, type, new YenAmount(amount));
    }

    public static SnapshotRate CategoryRate(RateType type, long amount)
    {
        return new(ServiceId, CategoryId, type, new YenAmount(amount));
    }


    public static SnapshotPremium PercentagePremium(
        int basisPoints,
        int? start = null,
        int? end = null,
        IEnumerable<DayOfWeek>? weekdays = null,
        bool usesNationalHolidays = false,
        IEnumerable<ServiceId>? services = null)
    {
        return Premium(
            PremiumCalculationType.Percentage,
            percentage: basisPoints,
            start: start,
            end: end,
            weekdays: weekdays,
            usesNationalHolidays: usesNationalHolidays,
            services: services);
    }

    public static SnapshotPremium FixedPerHourPremium(
        long amount,
        int? start = null,
        int? end = null,
        IEnumerable<DayOfWeek>? weekdays = null,
        bool usesNationalHolidays = false,
        IEnumerable<ServiceId>? services = null)
    {
        return Premium(
            PremiumCalculationType.FixedPerHour,
            amount: amount,
            start: start,
            end: end,
            weekdays: weekdays,
            usesNationalHolidays: usesNationalHolidays,
            services: services);
    }

    public static SnapshotPremium FixedPerRecordPremium(
        long amount,
        int? start = null,
        int? end = null,
        IEnumerable<DayOfWeek>? weekdays = null,
        bool usesNationalHolidays = false,
        IEnumerable<ServiceId>? services = null)
    {
        return Premium(
            PremiumCalculationType.FixedPerRecord,
            amount: amount,
            start: start,
            end: end,
            weekdays: weekdays,
            usesNationalHolidays: usesNationalHolidays,
            services: services);
    }

    public static SnapshotCountBonus CountBonus(long amount, IEnumerable<ServiceId>? services = null)
    {
        return new(
            new CountBonusId(Guid.NewGuid()),
            "件数加算",
            new YenAmount(amount),
            Set(services),
            true);
    }


    public static SettingSnapshot Snapshot(
        SnapshotRate? rate,
        IReadOnlyList<SnapshotPremium>? premiums = null,
        IReadOnlyList<SnapshotCountBonus>? bonuses = null,
        IReadOnlyList<SnapshotRate>? additionalRates = null)
    {
        var rates = new List<SnapshotRate>();
        if (rate is not null)
        {
            rates.Add(rate);
        }

        if (additionalRates is not null)
        {
            rates.AddRange(additionalRates);
        }

        return new SettingSnapshot(
            new SettingSnapshotId(Guid.NewGuid()),
            null,
            HolidayVersionId,
            new SchemaVersion(1),
            DateTimeOffset.Parse("2026-08-15T00:00:00Z"),
            [new SnapshotService(ServiceId, "身体", new DisplayOrder(0), true)],
            [
                new SnapshotTimeCategory(
                    CategoryId,
                    ServiceId,
                    "30分",
                    new WorkMinutes(30),
                    new DisplayOrder(0),
                    true),
            ],
            rates,
            premiums ?? [],
            bonuses ?? []);
    }

    public static WorkSalaryCalculationRequest Request(
        WorkRecord record,
        SettingSnapshot snapshot,
        IEnumerable<DateOnly>? holidays = null)
    {
        var holidayDictionary = (holidays ?? [])
            .ToDictionary(static date => date, static _ => "祝日");
        return new WorkSalaryCalculationRequest(
            record,
            snapshot,
            new HolidayCalendar(HolidayVersionId, holidayDictionary));
    }

    public static PayrollPeriod Period(int year, int month, DateOnly start, DateOnly end)
    {
        return new(new PayrollPeriodKey(new YearMonth(year, month)), start, end);
    }

    public static ClosingRule ClosingRule(int year, int month, int? closingDay)
    {
        return new(
            new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(year, month)),
            closingDay);
    }

    public static WorkSalaryCalculation CalculatedRecord(long total, Guid? recordId = null)
    {
        var id = new WorkRecordId(recordId ?? Guid.NewGuid());
        var taskId = new WorkTaskId(id.Value);
        var rate = Rate(RateType.FixedPerRecord, total);
        return new(
            id,
            SalaryCalculationStatus.Calculated,
            [new TaskSalaryCalculation(taskId, SalaryCalculationStatus.Calculated, rate,
                new YenAmount(total), [], new YenAmount(total), [])],
            [],
            new YenAmount(total),
            []);
    }


    private static SnapshotPremium Premium(
        PremiumCalculationType type,
        int? percentage = null,
        long? amount = null,
        int? start = null,
        int? end = null,
        IEnumerable<DayOfWeek>? weekdays = null,
        bool usesNationalHolidays = false,
        IEnumerable<ServiceId>? services = null)
    {
        return new SnapshotPremium(
            new PremiumId(Guid.NewGuid()),
            "割増",
            type,
            percentage is null ? null : new BasisPoints(percentage.Value),
            amount is null ? null : new YenAmount(amount.Value),
            start is null ? null : new MinuteOfDay(start.Value),
            end is null ? null : new MinuteOfDay(end.Value),
            usesNationalHolidays,
            Set(weekdays),
            Set<DateOnly>(null),
            Set(services),
            true);
    }

    private static IReadOnlySet<T> Set<T>(IEnumerable<T>? values)
    {
        return new HashSet<T>(values ?? []);
    }

}
