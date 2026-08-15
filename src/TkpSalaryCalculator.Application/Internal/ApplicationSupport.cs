using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Internal;

internal static class ApplicationSupport
{
    public static readonly IReadOnlyList<IssueDto> NoIssues = Array.Empty<IssueDto>();

    public static void ThrowIfCancellationRequested(CancellationToken cancellationToken) =>
        cancellationToken.ThrowIfCancellationRequested();

    public static YearMonth ToYearMonth(DateOnly date) => new(date.Year, date.Month);

    public static void ValidateYearMonth(YearMonth value, string parameterName)
    {
        if (value.Year is < 1 or > 9999 || value.Month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(parameterName, "年月を正しく指定してください。");
    }

    public static void ValidatePayrollPeriodKey(PayrollPeriodKey value, string parameterName) =>
        ValidateYearMonth(value.Value, parameterName);

    public static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty) throw new ArgumentException("識別子を指定してください。", parameterName);
    }

    public static WorkRecord ToDomain(WorkRecordDto value) => new(
        value.Id, value.WorkDate, value.ServiceId, value.TimeCategoryId, value.InputMode,
        value.WorkMinutes, value.StartTime, value.EndTime);

    public static IssueDto Issue(string code, string message, string? field = null) => new(code, field, message);

    public static ApplicationErrorException Invalid(string code, string message, string? field = null) =>
        new(code, message, field);

    public static (WorkMinutes? Minutes, MinuteOfDay? Start, MinuteOfDay? End, IReadOnlyList<IssueDto> Issues)
        Normalize(
            WorkInputMode mode,
            WorkMinutes? minutes,
            MinuteOfDay? start,
            MinuteOfDay? end,
            bool requiresStartTime)
    {
        var issues = new List<IssueDto>();
        if (!Enum.IsDefined(mode))
        {
            issues.Add(Issue("WORK_INPUT_MODE_INVALID", "勤務時間の入力方式を選び直してください。", "InputMode"));
            return (null, null, null, issues);
        }

        if (mode == WorkInputMode.TimeRange)
        {
            if (minutes is not null)
                issues.Add(Issue("WORK_MINUTES_NOT_ALLOWED", "開始・終了時刻方式では勤務時間を入力しないでください。", "WorkMinutes"));
            if (start is null)
                issues.Add(Issue("WORK_START_REQUIRED", "開始時刻を入力してください。", "StartTime"));
            if (end is null)
                issues.Add(Issue("WORK_END_REQUIRED", "終了時刻を入力してください。", "EndTime"));
            if (start is null || end is null || issues.Count != 0) return (null, start, end, issues);
            if (start.Value.Value is < 0 or > 1439 || end.Value.Value is < 0 or > 1439)
            {
                issues.Add(Issue("WORK_TIME_OUT_OF_RANGE", "時刻は0時00分から23時59分の範囲で指定してください。", "StartTime"));
                return (null, start, end, issues);
            }

            var value = end.Value.Value - start.Value.Value;
            if (value <= 0) value += 1440;
            return (new WorkMinutes(value), start, end, issues);
        }

        if (minutes is null || minutes.Value.Value is < 1 or > 1440)
            issues.Add(Issue("WORK_MINUTES_OUT_OF_RANGE", "勤務時間は1分以上24時間以内で入力してください。超える場合は複数の記録に分けてください。", "WorkMinutes"));
        if (end is not null)
            issues.Add(Issue("WORK_END_NOT_ALLOWED", "勤務時間方式の終了時刻は開始時刻から自動計算されるため入力しないでください。", "EndTime"));
        if (requiresStartTime && start is null)
            issues.Add(Issue("WORK_START_REQUIRED_FOR_PREMIUM", "時刻条件付き割増を計算するため開始時刻を入力してください。", "StartTime"));
        if (start is { Value: < 0 or > 1439 })
            issues.Add(Issue("WORK_TIME_OUT_OF_RANGE", "時刻は0時00分から23時59分の範囲で指定してください。", "StartTime"));
        if (issues.Count != 0 || minutes is null) return (minutes, start, null, issues);
        MinuteOfDay? normalizedEnd = start is null ? null : new MinuteOfDay((start.Value.Value + minutes.Value.Value) % 1440);
        return (minutes, start, normalizedEnd, issues);
    }

    public static bool RequiresStartTime(SettingSnapshot settings, ServiceId serviceId, DateOnly workDate,
        HolidayCalendar holidayCalendar) => settings.Premiums.Any(p => p.IsEnabled && p.StartTime is not null &&
            (p.ServiceIds.Count == 0 || p.ServiceIds.Contains(serviceId)) && MatchesDate(p, workDate, holidayCalendar));

    public static IReadOnlyList<IssueDto> ValidateSelection(
        SettingSnapshot settings,
        ServiceId serviceId,
        TimeCategoryId? timeCategoryId,
        bool allowDisabledExistingSelection = false)
    {
        var issues = new List<IssueDto>();
        var service = settings.Services.FirstOrDefault(x => x.Id == serviceId);
        if (service is null || (!service.IsEnabled && !allowDisabledExistingSelection))
            issues.Add(Issue("WORK_SERVICE_UNAVAILABLE", "選択したサービスはこの年月の新規勤務では使用できません。", "ServiceId"));
        if (timeCategoryId is { } categoryId)
        {
            var category = settings.TimeCategories.FirstOrDefault(x => x.Id == categoryId);
            if (category is null || category.ServiceId != serviceId || (!category.IsEnabled && !allowDisabledExistingSelection))
                issues.Add(Issue("WORK_TIME_CATEGORY_UNAVAILABLE", "選択した時間区分はこのサービスと年月では使用できません。", "TimeCategoryId"));
        }
        return issues;
    }

    public static IReadOnlyList<IssueDto> CalculationIssues(WorkSalaryCalculation calculation) =>
        calculation.MissingRequirements.Select(x => Issue(
            $"CALC_{x.Code}", "給与計算に必要な設定が不足しています。設定画面で内容を確認してください。"))
            .ToArray();

    public static async Task<WorkSalaryCalculation> CalculateAsync(
        WorkRecordDto record,
        SettingSnapshot snapshot,
        IHolidayCalendarRepository holidays,
        ISalaryCalculator calculator,
        CancellationToken cancellationToken)
    {
        var calendar = await holidays.GetAsync(snapshot.HolidayCalendarVersionId, cancellationToken).ConfigureAwait(false);
        return calculator.Calculate(new WorkSalaryCalculationRequest(ToDomain(record), ForCalculationDate(snapshot, record.WorkDate, calendar), calendar));
    }

    public static SettingSnapshot ForCalculationDate(SettingSnapshot snapshot, DateOnly workDate, HolidayCalendar holidayCalendar)
    {
        var filtered = snapshot.Premiums.Where(p => p.StartTime is null || MatchesDate(p, workDate, holidayCalendar)).ToArray();
        if (filtered.Length == snapshot.Premiums.Count) return snapshot;
        return new SettingSnapshot(snapshot.Id, snapshot.BasedOnId, snapshot.HolidayCalendarVersionId,
            snapshot.SchemaVersion, snapshot.CreatedAtUtc, snapshot.Services, snapshot.TimeCategories,
            snapshot.Rates, filtered, snapshot.CountBonuses);
    }

    private static bool MatchesDate(SnapshotPremium rule, DateOnly workDate, HolidayCalendar calendar)
    {
        var hasCondition = rule.Weekdays.Count != 0 || rule.UsesNationalHolidays || rule.Dates.Count != 0;
        return !hasCondition || rule.Weekdays.Contains(workDate.DayOfWeek) ||
            (rule.UsesNationalHolidays && calendar.Holidays.ContainsKey(workDate)) || rule.Dates.Contains(workDate);
    }

    public static async Task MarkChangedAsync(
        IAppMetadataRepository metadata,
        IUtcClock clock,
        CancellationToken cancellationToken) =>
        await metadata.SetLastDataChangedAtUtcAsync(clock.UtcNow.ToUniversalTime(), cancellationToken).ConfigureAwait(false);
}
