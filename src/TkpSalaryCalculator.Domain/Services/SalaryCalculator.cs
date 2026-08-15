using System.Collections.ObjectModel;
using System.Numerics;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Services;

/// <summary>不足している給与計算要件を識別する安定したコードを提供します。</summary>
public static class MissingCalculationRequirementCodes
{
    /// <summary>勤務が参照するサービスが設定スナップショットに存在しません。</summary>
    public const string Service = "MissingService";

    /// <summary>勤務が参照する時間区分が設定スナップショットに存在しないか、サービスと一致しません。</summary>
    public const string TimeCategory = "MissingTimeCategory";

    /// <summary>適用可能な基本単価が存在しません。</summary>
    public const string Rate = "MissingRate";

}

/// <summary>整数演算だけを使用して勤務記録、日別および給与期間の給与を計算します。</summary>
public sealed class SalaryCalculator : ISalaryCalculator
{
    /// <inheritdoc />
    public WorkSalaryCalculation Calculate(WorkSalaryCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.WorkRecord);
        ArgumentNullException.ThrowIfNull(request.SettingSnapshot);
        ArgumentNullException.ThrowIfNull(request.HolidayCalendar);

        var workRecord = request.WorkRecord;
        var snapshot = request.SettingSnapshot;
        var holidayCalendar = request.HolidayCalendar;
        if (snapshot.HolidayCalendarVersionId != holidayCalendar.VersionId)
        {
            throw new ArgumentException("設定スナップショットと祝日カレンダーのバージョンが一致しません。", nameof(request));
        }

        var missingRequirements = FindMissingRequirements(workRecord, snapshot, out var rate);
        if (missingRequirements.Count != 0)
        {
            return new WorkSalaryCalculation(
                workRecord.Id,
                SalaryCalculationStatus.Uncalculated,
                null,
                null,
                EmptyReadOnly<AppliedPremium>(),
                EmptyReadOnly<AppliedCountBonus>(),
                null,
                AsReadOnly(missingRequirements));
        }

        var appliedRate = rate!;
        var basePay = CalculateBasePay(appliedRate, workRecord.WorkMinutes);
        var premiums = CalculatePremiums(workRecord, snapshot.Premiums, holidayCalendar, appliedRate);
        var countBonuses = CalculateCountBonuses(workRecord.ServiceId, snapshot.CountBonuses);
        var total = MoneyMath.Sum(
            new[] { basePay.Value }
                .Concat(premiums.Select(static item => item.Amount.Value))
                .Concat(countBonuses.Select(static item => item.Amount.Value)));

        return new WorkSalaryCalculation(
            workRecord.Id,
            SalaryCalculationStatus.Calculated,
            appliedRate,
            basePay,
            AsReadOnly(premiums),
            AsReadOnly(countBonuses),
            new YenAmount(total),
            EmptyReadOnly<MissingCalculationRequirement>());
    }

    /// <inheritdoc />
    public DailySalaryCalculation AggregateDay(
        DateOnly workDate,
        IReadOnlyList<WorkSalaryCalculation> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var recordCopy = CopyAndValidateRecords(records);
        var calculatedRecords = recordCopy
            .Where(static record => record.Status == SalaryCalculationStatus.Calculated)
            .ToArray();

        var basePaySubtotal = MoneyMath.Sum(calculatedRecords.Select(static record => record.BasePay!.Value.Value));
        var premiumSubtotal = MoneyMath.Sum(calculatedRecords.SelectMany(static record => record.Premiums).Select(static item => item.Amount.Value));
        var countBonusSubtotal = MoneyMath.Sum(calculatedRecords.SelectMany(static record => record.CountBonuses).Select(static item => item.Amount.Value));
        var calculatedSubtotal = MoneyMath.Sum(calculatedRecords.Select(static record => record.Total!.Value.Value));

        return new DailySalaryCalculation(
            workDate,
            recordCopy,
            new YenAmount(basePaySubtotal),
            new YenAmount(premiumSubtotal),
            new YenAmount(countBonusSubtotal),
            new YenAmount(calculatedSubtotal),
            recordCopy.Count - calculatedRecords.Length);
    }

    /// <inheritdoc />
    public PayrollPeriodSalaryCalculation AggregatePeriod(
        PayrollPeriod period,
        IReadOnlyList<DailySalaryCalculation> days,
        IReadOnlyList<MonthlyAllowance> allowances)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(allowances);

        var dayCopy = CopyAndValidateDays(period, days);
        var allowanceCopy = CopyAndValidateAllowances(period, allowances);
        var basePaySubtotal = MoneyMath.Sum(dayCopy.Select(static day => day.BasePaySubtotal.Value));
        var premiumSubtotal = MoneyMath.Sum(dayCopy.Select(static day => day.PremiumSubtotal.Value));
        var countBonusSubtotal = MoneyMath.Sum(dayCopy.Select(static day => day.CountBonusSubtotal.Value));
        var allowanceSubtotal = MoneyMath.Sum(allowanceCopy.Select(static allowance => allowance.Amount.Value));
        var calculatedSubtotal = MoneyMath.Sum(
            dayCopy.Select(static day => day.CalculatedSubtotal.Value)
                .Concat([allowanceSubtotal]));

        return new PayrollPeriodSalaryCalculation(
            period,
            dayCopy,
            allowanceCopy,
            new YenAmount(basePaySubtotal),
            new YenAmount(premiumSubtotal),
            new YenAmount(countBonusSubtotal),
            new YenAmount(allowanceSubtotal),
            new YenAmount(calculatedSubtotal),
            checked(dayCopy.Sum(static day => day.UncalculatedCount)));
    }

    private static List<MissingCalculationRequirement> FindMissingRequirements(
        WorkRecord workRecord,
        SettingSnapshot snapshot,
        out SnapshotRate? rate)
    {
        var missing = new List<MissingCalculationRequirement>();
        var serviceExists = snapshot.Services.Any(service => service.Id == workRecord.ServiceId);
        if (!serviceExists)
        {
            missing.Add(new MissingCalculationRequirement(
                MissingCalculationRequirementCodes.Service,
                workRecord.ServiceId.Value));
        }

        var timeCategoryExists = true;
        if (workRecord.TimeCategoryId is { } timeCategoryId)
        {
            timeCategoryExists = snapshot.TimeCategories.Any(
                category => category.Id == timeCategoryId && category.ServiceId == workRecord.ServiceId);
            if (!timeCategoryExists)
            {
                missing.Add(new MissingCalculationRequirement(
                    MissingCalculationRequirementCodes.TimeCategory,
                    timeCategoryId.Value));
            }
        }

        rate = null;
        if (serviceExists && timeCategoryExists)
        {
            if (workRecord.TimeCategoryId is { } selectedTimeCategoryId)
            {
                rate = snapshot.Rates.SingleOrDefault(
                    candidate => candidate.ServiceId == workRecord.ServiceId &&
                                 candidate.TimeCategoryId == selectedTimeCategoryId);
            }

            rate ??= snapshot.Rates.SingleOrDefault(
                candidate => candidate.ServiceId == workRecord.ServiceId &&
                             candidate.TimeCategoryId is null);
        }

        if (rate is null)
        {
            missing.Add(new MissingCalculationRequirement(
                MissingCalculationRequirementCodes.Rate,
                workRecord.TimeCategoryId?.Value ?? workRecord.ServiceId.Value));
        }

        if (workRecord.StartTime is null)
        {
            var timeConditionedPremium = snapshot.Premiums.FirstOrDefault(
                premium => premium.IsEnabled &&
                           premium.StartTime is not null &&
                           MatchesService(premium.ServiceIds, workRecord.ServiceId));
            if (timeConditionedPremium is not null)
            {
                throw new ArgumentException(
                    $"割増ルール '{timeConditionedPremium.DisplayName}' の判定に必要な開始時刻がありません。",
                    nameof(workRecord));
            }
        }

        return missing;
    }

    private static YenAmount CalculateBasePay(SnapshotRate rate, WorkMinutes workMinutes)
    {
        var value = rate.RateType switch
        {
            RateType.Hourly => MoneyMath.CeilProduct(rate.Amount.Value, workMinutes.Value, 60),
            RateType.FixedPerRecord => rate.Amount.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(rate), "未対応の単価方式です。"),
        };

        return new YenAmount(value);
    }

    private static List<AppliedPremium> CalculatePremiums(
        WorkRecord workRecord,
        IReadOnlyList<SnapshotPremium> rules,
        HolidayCalendar holidayCalendar,
        SnapshotRate rate)
    {
        var applied = new List<AppliedPremium>();
        foreach (var rule in rules)
        {
            if (!rule.IsEnabled ||
                !MatchesService(rule.ServiceIds, workRecord.ServiceId) ||
                !MatchesDate(rule, workRecord.WorkDate, holidayCalendar))
            {
                continue;
            }

            var applicableMinutes = GetApplicableMinutes(workRecord, rule);
            if (applicableMinutes == 0)
            {
                continue;
            }

            var amount = rule.CalculationType switch
            {
                PremiumCalculationType.Percentage => CalculatePercentagePremium(
                    rate,
                    workRecord.WorkMinutes,
                    applicableMinutes,
                    rule.Percentage!.Value),
                PremiumCalculationType.FixedPerHour => MoneyMath.CeilProduct(
                    rule.Amount!.Value.Value,
                    applicableMinutes,
                    60),
                PremiumCalculationType.FixedPerRecord => rule.Amount!.Value.Value,
                _ => throw new ArgumentOutOfRangeException(nameof(rule), "未対応の割増方式です。"),
            };

            applied.Add(new AppliedPremium(
                rule,
                new WorkMinutes(applicableMinutes),
                new YenAmount(amount)));
        }

        return applied;
    }

    private static long CalculatePercentagePremium(
        SnapshotRate rate,
        WorkMinutes workMinutes,
        int applicableMinutes,
        BasisPoints percentage)
    {
        var applicableBasePay = rate.RateType switch
        {
            RateType.Hourly => MoneyMath.CeilProduct(rate.Amount.Value, applicableMinutes, 60),
            RateType.FixedPerRecord => MoneyMath.CeilProduct(
                rate.Amount.Value,
                applicableMinutes,
                workMinutes.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(rate), "未対応の単価方式です。"),
        };

        return MoneyMath.CeilProduct(applicableBasePay, percentage.Value, 10000);
    }

    private static List<AppliedCountBonus> CalculateCountBonuses(
        ServiceId serviceId,
        IReadOnlyList<SnapshotCountBonus> rules)
    {
        return [.. rules
            .Where(rule => rule.IsEnabled && MatchesService(rule.ServiceIds, serviceId))
            .Select(rule => new AppliedCountBonus(rule.Id, rule.DisplayName, rule.Amount))];
    }

    private static bool MatchesService(IReadOnlySet<ServiceId> serviceIds, ServiceId serviceId)
    {
        return serviceIds.Count == 0 || serviceIds.Contains(serviceId);
    }


    private static bool MatchesDate(
        SnapshotPremium rule,
        DateOnly workDate,
        HolidayCalendar holidayCalendar)
    {
        var hasDateCondition = rule.Weekdays.Count != 0 || rule.UsesNationalHolidays || rule.Dates.Count != 0;
        return !hasDateCondition ||
               rule.Weekdays.Contains(workDate.DayOfWeek) ||
               (rule.UsesNationalHolidays && holidayCalendar.Holidays.ContainsKey(workDate)) ||
               rule.Dates.Contains(workDate);
    }

    private static int GetApplicableMinutes(WorkRecord workRecord, SnapshotPremium rule)
    {
        if (rule.StartTime is null)
        {
            return workRecord.WorkMinutes.Value;
        }

        if (workRecord.StartTime is null)
        {
            throw new InvalidOperationException("時刻条件付き割増の勤務区間が検証されていません。");
        }

        var workStart = workRecord.StartTime.Value.Value;
        var workEnd = workStart + workRecord.WorkMinutes.Value;
        var ruleStart = rule.StartTime.Value.Value;
        var ruleEnd = rule.EndTime!.Value.Value;
        var crossesMidnight = ruleEnd <= ruleStart;
        var applicable = 0;

        for (var dayOffset = -1; dayOffset <= 1; dayOffset++)
        {
            var windowStart = (dayOffset * 1440) + ruleStart;
            var windowEnd = (dayOffset * 1440) + ruleEnd + (crossesMidnight ? 1440 : 0);
            var overlapStart = Math.Max(workStart, windowStart);
            var overlapEnd = Math.Min(workEnd, windowEnd);
            if (overlapEnd > overlapStart)
            {
                applicable = checked(applicable + (overlapEnd - overlapStart));
            }
        }

        if (applicable > workRecord.WorkMinutes.Value)
        {
            throw new InvalidOperationException("割増対象時間が勤務時間を超えました。");
        }

        return applicable;
    }

    private static IReadOnlyList<WorkSalaryCalculation> CopyAndValidateRecords(
        IReadOnlyList<WorkSalaryCalculation> records)
    {
        var copy = Copy(records, nameof(records));
        var ids = new HashSet<WorkRecordId>();
        var normalized = new List<WorkSalaryCalculation>(copy.Count);
        foreach (var record in copy)
        {
            DomainIdGuard.NotEmpty(record.WorkRecordId.Value, nameof(records));
            if (!ids.Add(record.WorkRecordId))
            {
                throw new ArgumentException("同じ勤務記録の計算結果が重複しています。", nameof(records));
            }

            ValidateRecordCalculation(record, nameof(records));
            normalized.Add(new WorkSalaryCalculation(
                record.WorkRecordId,
                record.Status,
                record.AppliedRate,
                record.BasePay,
                AsReadOnly(record.Premiums),
                AsReadOnly(record.CountBonuses),
                record.Total,
                AsReadOnly(record.MissingRequirements)));
        }

        return AsReadOnly(normalized);
    }

    private static void ValidateRecordCalculation(WorkSalaryCalculation record, string parameterName)
    {
        if (!Enum.IsDefined(record.Status))
        {
            throw new ArgumentException("給与計算状態が不正です。", parameterName);
        }

        ArgumentNullException.ThrowIfNull(record.Premiums, parameterName);
        ArgumentNullException.ThrowIfNull(record.CountBonuses, parameterName);
        ArgumentNullException.ThrowIfNull(record.MissingRequirements, parameterName);
        if (record.Premiums.Any(static item => item is null) ||
            record.CountBonuses.Any(static item => item is null) ||
            record.MissingRequirements.Any(static item => item is null))
        {
            throw new ArgumentException("計算結果のコレクションにnullを含めることはできません。", parameterName);
        }

        foreach (var missingRequirement in record.MissingRequirements)
        {
            if (string.IsNullOrWhiteSpace(missingRequirement.Code))
            {
                throw new ArgumentException("不足要件コードは空白にできません。", parameterName);
            }

            if (missingRequirement.RelatedId == Guid.Empty)
            {
                throw new ArgumentException("不足要件の関連識別子に空のGUIDは使用できません。", parameterName);
            }
        }

        if (record.Status == SalaryCalculationStatus.Uncalculated)
        {
            if (record.AppliedRate is not null || record.BasePay is not null || record.Total is not null ||
                record.Premiums.Count != 0 || record.CountBonuses.Count != 0 || record.MissingRequirements.Count == 0)
            {
                throw new ArgumentException("未計算結果に推測額を含めず、不足要件を1件以上指定してください。", parameterName);
            }

            return;
        }

        if (record.AppliedRate is null || record.BasePay is null || record.Total is null || record.MissingRequirements.Count != 0)
        {
            throw new ArgumentException("計算済み結果に必要な単価、金額または内訳がありません。", parameterName);
        }

        DomainValueGuard.NonNegative(record.BasePay.Value, parameterName);
        DomainValueGuard.NonNegative(record.Total.Value, parameterName);
        foreach (var premium in record.Premiums)
        {
            ArgumentNullException.ThrowIfNull(premium.Rule, parameterName);
            DomainValueGuard.ValidWorkMinutes(premium.ApplicableMinutes, parameterName);
            DomainValueGuard.NonNegative(premium.Amount, parameterName);
        }

        foreach (var countBonus in record.CountBonuses)
        {
            DomainIdGuard.NotEmpty(countBonus.CountBonusId.Value, parameterName);
            DomainModelGuard.NotBlank(countBonus.DisplayName, parameterName);
            DomainValueGuard.NonNegative(countBonus.Amount, parameterName);
        }

        var expectedTotal = MoneyMath.Sum(
            new[] { record.BasePay.Value.Value }
                .Concat(record.Premiums.Select(static item => item.Amount.Value))
                .Concat(record.CountBonuses.Select(static item => item.Amount.Value)));
        if (expectedTotal != record.Total.Value.Value)
        {
            throw new ArgumentException("勤務記録合計が内訳の合計と一致しません。", parameterName);
        }
    }

    private static IReadOnlyList<DailySalaryCalculation> CopyAndValidateDays(
        PayrollPeriod period,
        IReadOnlyList<DailySalaryCalculation> days)
    {
        var copy = Copy(days, nameof(days));
        var dates = new HashSet<DateOnly>();
        var normalized = new List<DailySalaryCalculation>(copy.Count);
        foreach (var day in copy)
        {
            if (!period.Contains(day.WorkDate))
            {
                throw new ArgumentException("給与期間外の日別結果が含まれています。", nameof(days));
            }

            if (!dates.Add(day.WorkDate))
            {
                throw new ArgumentException("同じ勤務日の日別結果が重複しています。", nameof(days));
            }

            var recalculated = new SalaryCalculator().AggregateDay(day.WorkDate, day.Records);
            if (recalculated.BasePaySubtotal != day.BasePaySubtotal ||
                recalculated.PremiumSubtotal != day.PremiumSubtotal ||
                recalculated.CountBonusSubtotal != day.CountBonusSubtotal ||
                recalculated.CalculatedSubtotal != day.CalculatedSubtotal ||
                recalculated.UncalculatedCount != day.UncalculatedCount)
            {
                throw new ArgumentException("日別結果が勤務記録の内訳と一致しません。", nameof(days));
            }

            normalized.Add(recalculated);
        }

        return AsReadOnly(normalized);
    }

    private static IReadOnlyList<MonthlyAllowance> CopyAndValidateAllowances(
        PayrollPeriod period,
        IReadOnlyList<MonthlyAllowance> allowances)
    {
        var copy = Copy(allowances, nameof(allowances));
        var ids = new HashSet<MonthlyAllowanceId>();
        foreach (var allowance in copy)
        {
            if (allowance.PayrollPeriodKey != period.Key)
            {
                throw new ArgumentException("別の給与期間の月額手当が含まれています。", nameof(allowances));
            }

            if (!ids.Add(allowance.Id))
            {
                throw new ArgumentException("同じ月額手当が重複しています。", nameof(allowances));
            }
        }

        return copy;
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string parameterName)
        where T : class
    {
        var copy = values.ToArray();
        if (copy.Any(static item => item is null))
        {
            throw new ArgumentException("コレクションにnullを含めることはできません。", parameterName);
        }

        return new ReadOnlyCollection<T>(copy);
    }

    private static IReadOnlyList<T> AsReadOnly<T>(IEnumerable<T> values)
    {
        return new ReadOnlyCollection<T>([.. values]);
    }


    private static IReadOnlyList<T> EmptyReadOnly<T>()
    {
        return [];
    }

}

internal static class MoneyMath
{
    public static long CeilProduct(long first, int second, int divisor)
    {
        if (first < 0 || second < 0 || divisor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(first), "切り上げ除算には非負の被除数と正の除数が必要です。");
        }

        return ToInt64(((BigInteger)first * second + divisor - 1) / divisor);
    }

    public static long Sum(IEnumerable<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var total = BigInteger.Zero;
        foreach (var value in values)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(values), "金額の合計に負数を含めることはできません。");
            }

            total += value;
        }

        return ToInt64(total);
    }

    private static long ToInt64(BigInteger value)
    {
        if (value > long.MaxValue)
        {
            throw new OverflowException("給与計算結果が整数円で表現できる範囲を超えました。");
        }

        return (long)value;
    }
}
