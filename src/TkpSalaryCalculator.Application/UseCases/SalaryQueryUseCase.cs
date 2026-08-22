using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Internal;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.UseCases;

/// <summary>勤務開始日の暦月設定を選択して、日別・暦月・給与期間の読取モデルを構築します。</summary>
/// <remarks>必要な読取ポートとドメインサービスを指定して生成します。</remarks>
public sealed class SalaryQueryUseCase(IWorkRecordRepository records, ISettingSnapshotRepository settings,
    IHolidayCalendarRepository holidays, IClosingRuleRepository closingRules,
    IMonthlyAllowanceRepository allowances, IBasicShiftRepository shifts,
    ISalaryCalculator calculator, IPayrollPeriodCalculator periodCalculator) : ISalaryQueryUseCase
{
    private readonly IWorkRecordRepository records = records ?? throw new ArgumentNullException(nameof(records));
    private readonly ISettingSnapshotRepository settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IHolidayCalendarRepository holidays = holidays ?? throw new ArgumentNullException(nameof(holidays));
    private readonly IClosingRuleRepository closingRules = closingRules ?? throw new ArgumentNullException(nameof(closingRules));
    private readonly IMonthlyAllowanceRepository allowances = allowances ?? throw new ArgumentNullException(nameof(allowances));
    private readonly IBasicShiftRepository shifts = shifts ?? throw new ArgumentNullException(nameof(shifts));
    private readonly ISalaryCalculator calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
    private readonly IPayrollPeriodCalculator periodCalculator = periodCalculator ?? throw new ArgumentNullException(nameof(periodCalculator));


    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarDayDto>> GetCalendarMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidateYearMonth(yearMonth, nameof(yearMonth));
        cancellationToken.ThrowIfCancellationRequested();
        var start = new DateOnly(yearMonth.Year, yearMonth.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var byDate = new Dictionary<DateOnly, List<WorkRecordDto>>();
        await foreach (var record in records.StreamRangeAsync(start, end, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!byDate.TryGetValue(record.WorkDate, out var list)) byDate[record.WorkDate] = list = [];
            list.Add(record);
        }
        var result = new List<CalendarDayDto>(end.Day);
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var dayRecords = byDate.TryGetValue(date, out var values) ? values : [];
            var daily = await CalculateDayAsync(date, dayRecords, cancellationToken).ConfigureAwait(false);
            var weekdayShifts = await shifts.GetForWeekdayAsync(date.DayOfWeek, cancellationToken).ConfigureAwait(false);
            var appliedIds = dayRecords.Where(x => x.SourceBasicShiftId is not null).Select(x => x.SourceBasicShiftId!.Value).ToHashSet();
            var candidateCount = weekdayShifts.Count(x => x.IsEnabled && !appliedIds.Contains(x.Id));
            result.Add(new(date, dayRecords.Count, daily.CalculatedSubtotal, daily.UncalculatedCount, candidateCount));
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<DailySalaryDto> GetDayAsync(DateOnly workDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = new List<WorkRecordDto>();
        await foreach (var item in records.StreamRangeAsync(workDate, workDate, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false)) values.Add(item);
        return await CalculateDayAsync(workDate, values, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PayrollPeriodSummaryDto> GetPayrollPeriodAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidatePayrollPeriodKey(payrollPeriodKey, nameof(payrollPeriodKey));
        cancellationToken.ThrowIfCancellationRequested();
        var history = ClosingRuleHistorySupport.ForCalculation(
            await closingRules.GetHistoryAsync(cancellationToken).ConfigureAwait(false));
        var period = periodCalculator.GetPeriod(payrollPeriodKey, history);
        var byDate = new SortedDictionary<DateOnly, List<WorkRecordDto>>();
        await foreach (var record in records.StreamRangeAsync(period.StartDate, period.EndDate, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!byDate.TryGetValue(record.WorkDate, out var list)) byDate[record.WorkDate] = list = [];
            list.Add(record);
        }
        var days = new List<DailySalaryDto>();
        var domainDays = new List<DailySalaryCalculation>();
        foreach (var pair in byDate)
        {
            var day = await CalculateDayAsync(pair.Key, pair.Value, cancellationToken).ConfigureAwait(false);
            days.Add(day);
            domainDays.Add(ToDomain(day));
        }
        var periodAllowances = await allowances.GetForPeriodAsync(payrollPeriodKey, cancellationToken).ConfigureAwait(false);
        var aggregate = calculator.AggregatePeriod(period, domainDays, periodAllowances);
        return new(aggregate.Period, days,
            [.. periodAllowances.Select(x => new MonthlyAllowanceDto(x.Id, x.DisplayName, x.Amount))],
            aggregate.BasePaySubtotal, aggregate.PremiumSubtotal, aggregate.CountBonusSubtotal,
            aggregate.AllowanceSubtotal, aggregate.CalculatedSubtotal, aggregate.UncalculatedCount);
    }

    private async Task<DailySalaryDto> CalculateDayAsync(DateOnly date, IReadOnlyList<WorkRecordDto> values, CancellationToken cancellationToken)
    {
        var snapshot = await settings.GetEffectiveForMonthAsync(ApplicationSupport.ToYearMonth(date), cancellationToken).ConfigureAwait(false);
        var holiday = await holidays.GetAsync(snapshot.HolidayCalendarVersionId, cancellationToken).ConfigureAwait(false);
        var calculated = values.Select(value => calculator.Calculate(
            new WorkSalaryCalculationRequest(ApplicationSupport.ToDomain(value),
                ApplicationSupport.ForCalculationDate(snapshot, value.WorkDate, holiday), holiday))).ToArray();
        var aggregate = calculator.AggregateDay(date, calculated);
        var serviceNames = snapshot.Services.ToDictionary(x => x.Id, x => x.DisplayName);
        var categoryNames = snapshot.TimeCategories.ToDictionary(x => x.Id, x => x.DisplayName);
        var settingMonth = ApplicationSupport.ToYearMonth(date);
        return new(date, [.. values.Zip(calculated, (value, result) => new WorkRecordSalaryDto(
                value,
                result,
                serviceNames.GetValueOrDefault(value.ServiceId),
                value.TimeCategoryId is { } categoryId ? categoryNames.GetValueOrDefault(categoryId) : null,
                settingMonth))],
            aggregate.BasePaySubtotal, aggregate.PremiumSubtotal, aggregate.CountBonusSubtotal,
            aggregate.CalculatedSubtotal, aggregate.UncalculatedCount);
    }

    private static DailySalaryCalculation ToDomain(DailySalaryDto value)
    {
        return new(value.Date,
        [.. value.Records.Select(x => x.Calculation)], value.BasePaySubtotal, value.PremiumSubtotal,
        value.CountBonusSubtotal, value.CalculatedSubtotal, value.UncalculatedCount);
    }

}
