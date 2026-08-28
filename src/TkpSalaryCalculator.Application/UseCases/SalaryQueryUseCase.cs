using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
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
    ISalaryCalculator calculator, IPayrollPeriodCalculator periodCalculator,
    IAnnualSalaryCalculator annualCalculator) : ISalaryQueryUseCase
{
    private readonly IWorkRecordRepository records = records ?? throw new ArgumentNullException(nameof(records));
    private readonly ISettingSnapshotRepository settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IHolidayCalendarRepository holidays = holidays ?? throw new ArgumentNullException(nameof(holidays));
    private readonly IClosingRuleRepository closingRules = closingRules ?? throw new ArgumentNullException(nameof(closingRules));
    private readonly IMonthlyAllowanceRepository allowances = allowances ?? throw new ArgumentNullException(nameof(allowances));
    private readonly IBasicShiftRepository shifts = shifts ?? throw new ArgumentNullException(nameof(shifts));
    private readonly ISalaryCalculator calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
    private readonly IPayrollPeriodCalculator periodCalculator = periodCalculator ?? throw new ArgumentNullException(nameof(periodCalculator));
    private readonly IAnnualSalaryCalculator annualCalculator = annualCalculator ?? throw new ArgumentNullException(nameof(annualCalculator));

    /// <summary>年間集計サービスの標準実装を使用して生成します。</summary>
    public SalaryQueryUseCase(
        IWorkRecordRepository records,
        ISettingSnapshotRepository settings,
        IHolidayCalendarRepository holidays,
        IClosingRuleRepository closingRules,
        IMonthlyAllowanceRepository allowances,
        IBasicShiftRepository shifts,
        ISalaryCalculator calculator,
        IPayrollPeriodCalculator periodCalculator)
        : this(records, settings, holidays, closingRules, allowances, shifts, calculator, periodCalculator,
            new Domain.Services.AnnualSalaryCalculator())
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarDayDto>> GetCalendarMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken)
    {
        var (days, _) = await LoadCalendarMonthAsync(yearMonth, null, cancellationToken).ConfigureAwait(false);
        return days;
    }

    /// <inheritdoc />
    public async Task<CalendarMonthScreenDto> GetCalendarMonthScreenAsync(
        YearMonth yearMonth,
        DateOnly selectedDate,
        CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidateYearMonth(yearMonth, nameof(yearMonth));
        if (selectedDate.Year != yearMonth.Year || selectedDate.Month != yearMonth.Month)
            throw new ArgumentOutOfRangeException(nameof(selectedDate), "選択日は表示月の範囲内で指定してください。");

        var (days, selectedDay) = await LoadCalendarMonthAsync(yearMonth, selectedDate, cancellationToken)
            .ConfigureAwait(false);
        return new(days, selectedDay!);
    }

    private async Task<(IReadOnlyList<CalendarDayDto> Days, DailySalaryDto? SelectedDay)> LoadCalendarMonthAsync(
        YearMonth yearMonth,
        DateOnly? selectedDate,
        CancellationToken cancellationToken)
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
        var calculationContext = await LoadCalculationContextAsync(byDate.Keys, cancellationToken).ConfigureAwait(false);
        var requestedWeekdays = Enumerable.Range(0, end.Day)
            .Select(offset => start.AddDays(offset).DayOfWeek).Distinct().ToArray();
        var shiftsByWeekday = await shifts.GetForWeekdaysAsync(requestedWeekdays, cancellationToken).ConfigureAwait(false);
        return await RunCalculationAsync(() =>
        {
            var result = new List<CalendarDayDto>(end.Day);
            DailySalaryDto? selectedDay = null;
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dayRecords = byDate.TryGetValue(date, out var values) ? values : [];
                var daily = CalculateDay(date, dayRecords, calculationContext);
                if (date == selectedDate) selectedDay = daily;
                var weekdayShifts = shiftsByWeekday[date.DayOfWeek];
                var appliedIds = dayRecords.Where(x => x.SourceBasicShiftId is not null)
                    .Select(x => x.SourceBasicShiftId!.Value).ToHashSet();
                var candidateCount = weekdayShifts.Count(x => x.IsEnabled && !appliedIds.Contains(x.Id));
                result.Add(new(date, dayRecords.Count, daily.CalculatedSubtotal, daily.UncalculatedCount,
                    candidateCount));
            }
            return ((IReadOnlyList<CalendarDayDto>)result, selectedDay);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DayScreenDto> GetDayScreenAsync(DateOnly workDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = new List<WorkRecordDto>();
        await foreach (var item in records.StreamRangeAsync(workDate, workDate, cancellationToken)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
            values.Add(item);

        var month = ApplicationSupport.ToYearMonth(workDate);
        var snapshot = await settings.GetEffectiveForMonthAsync(month, cancellationToken).ConfigureAwait(false);
        var calendar = await holidays.GetAsync(snapshot.HolidayCalendarVersionId, cancellationToken).ConfigureAwait(false);
        var sourceShifts = await shifts.GetForWeekdayAsync(workDate.DayOfWeek, cancellationToken).ConfigureAwait(false);
        var context = new SalaryCalculationContext(
            new Dictionary<YearMonth, SettingSnapshot> { [month] = snapshot },
            new Dictionary<HolidayCalendarVersionId, HolidayCalendar> { [calendar.VersionId] = calendar });
        return await RunCalculationAsync(() =>
        {
            var daily = CalculateDay(workDate, values, context);
            var shiftPreview = BasicShiftUseCase.BuildPreview(
                workDate, sourceShifts, values, snapshot, calendar, calculator);
            return new DayScreenDto(daily, new MonthSettingsDto(month, snapshot), shiftPreview);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DailySalaryDto> GetDayAsync(DateOnly workDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = new List<WorkRecordDto>();
        await foreach (var item in records.StreamRangeAsync(workDate, workDate, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false)) values.Add(item);
        var context = await LoadCalculationContextAsync(values.Count == 0 ? [] : [workDate], cancellationToken)
            .ConfigureAwait(false);
        return await RunCalculationAsync(() => CalculateDay(workDate, values, context), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WorkRecordCalculationDto> GetWorkRecordCalculationAsync(
        WorkRecordId workRecordId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = await records.FindAsync(workRecordId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(
                "WORK_NOT_FOUND",
                "計算内訳を表示する勤務記録が見つかりませんでした。");
        var settingMonth = ApplicationSupport.ToYearMonth(record.WorkDate);
        var snapshot = await settings.GetEffectiveForMonthAsync(settingMonth, cancellationToken)
            .ConfigureAwait(false);
        var holiday = await holidays.GetAsync(snapshot.HolidayCalendarVersionId, cancellationToken)
            .ConfigureAwait(false);
        var closingRuleHistory = ClosingRuleHistorySupport.ForCalculation(
            await closingRules.GetHistoryAsync(cancellationToken).ConfigureAwait(false));

        return await RunCalculationAsync(() =>
        {
            var calculationSnapshot = ApplicationSupport.ForCalculationDate(snapshot, record.WorkDate, holiday);
            var calculation = calculator.Calculate(new WorkSalaryCalculationRequest(
                ApplicationSupport.ToDomain(record), calculationSnapshot, holiday));
            var serviceName = snapshot.Services.FirstOrDefault(x => x.Id == record.ServiceId)?.DisplayName;
            var categoryName = record.TimeCategoryId is { } categoryId
                ? snapshot.TimeCategories.FirstOrDefault(x => x.Id == categoryId)?.DisplayName
                : null;
            var period = periodCalculator.FindPeriod(record.WorkDate, closingRuleHistory);
            return new WorkRecordCalculationDto(
                period,
                new WorkRecordSalaryDto(record, calculation, serviceName, categoryName, settingMonth));
        }, cancellationToken).ConfigureAwait(false);
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
        var calculationContext = await LoadCalculationContextAsync(byDate.Keys, cancellationToken).ConfigureAwait(false);
        var periodAllowances = await allowances.GetForPeriodAsync(payrollPeriodKey, cancellationToken).ConfigureAwait(false);
        return await RunCalculationAsync(() =>
        {
            var days = new List<DailySalaryDto>();
            var domainDays = new List<DailySalaryCalculation>();
            foreach (var pair in byDate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var day = CalculateDay(pair.Key, pair.Value, calculationContext);
                days.Add(day);
                domainDays.Add(ToDomain(day));
            }
            var aggregate = calculator.AggregatePeriod(period, domainDays, periodAllowances);
            return new PayrollPeriodSummaryDto(aggregate.Period, days,
                [.. periodAllowances.Select(x => new MonthlyAllowanceDto(x.Id, x.DisplayName, x.Amount))],
                aggregate.BasePaySubtotal, aggregate.PremiumSubtotal, aggregate.CountBonusSubtotal,
                aggregate.AllowanceSubtotal, aggregate.CalculatedSubtotal, aggregate.UncalculatedCount);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<HomeSalarySummaryDto> GetHomeSalarySummaryAsync(
        PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken)
    {
        ApplicationSupport.ValidatePayrollPeriodKey(payrollPeriodKey, nameof(payrollPeriodKey));
        cancellationToken.ThrowIfCancellationRequested();

        var annualRange = annualCalculator.GetPeriodRange(payrollPeriodKey, AnnualClosingMonth.Default);
        var history = ClosingRuleHistorySupport.ForCalculation(
            await closingRules.GetHistoryAsync(cancellationToken).ConfigureAwait(false));
        var periodKeys = GetPeriodKeys(annualRange.Start, annualRange.AccumulationEnd);
        var periods = periodKeys.Select(key => periodCalculator.GetPeriod(key, history)).ToArray();

        var byDate = new SortedDictionary<DateOnly, List<WorkRecordDto>>();
        await foreach (var record in records.StreamRangeAsync(
            periods[0].StartDate,
            periods[^1].EndDate,
            cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!byDate.TryGetValue(record.WorkDate, out var list))
            {
                byDate[record.WorkDate] = list = [];
            }

            list.Add(record);
        }

        var calculationContext = await LoadCalculationContextAsync(byDate.Keys, cancellationToken)
            .ConfigureAwait(false);
        var rangeAllowances = await allowances.GetForRangeAsync(
            annualRange.Start,
            annualRange.AccumulationEnd,
            cancellationToken).ConfigureAwait(false);

        return await RunCalculationAsync(() =>
        {
            var daysByPeriod = periodKeys.ToDictionary(
                static key => key,
                static _ => new List<DailySalaryCalculation>());
            var selectedDays = new List<DailySalaryDto>();
            var periodIndex = 0;
            foreach (var pair in byDate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (periodIndex < periods.Length - 1 && pair.Key > periods[periodIndex].EndDate)
                {
                    periodIndex++;
                }

                var period = periods[periodIndex];
                if (!period.Contains(pair.Key))
                {
                    throw new InvalidOperationException("年間集計範囲の勤務日を給与期間へ割り当てられませんでした。");
                }

                var day = CalculateDay(pair.Key, pair.Value, calculationContext);
                daysByPeriod[period.Key].Add(ToDomain(day));
                if (period.Key == payrollPeriodKey)
                {
                    selectedDays.Add(day);
                }
            }

            var allowancesByPeriod = rangeAllowances
                .GroupBy(static allowance => allowance.PayrollPeriodKey)
                .ToDictionary(
                    static group => group.Key,
                    static group => (IReadOnlyList<MonthlyAllowance>)group.ToArray());
            var periodCalculations = new List<PayrollPeriodSalaryCalculation>(periods.Length);
            foreach (var period in periods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var periodAllowances = allowancesByPeriod.TryGetValue(period.Key, out var values)
                    ? values
                    : [];
                periodCalculations.Add(calculator.AggregatePeriod(
                    period,
                    daysByPeriod[period.Key],
                    periodAllowances));
            }

            var monthlyCalculation = periodCalculations[^1];
            var monthlyAllowances = allowancesByPeriod.TryGetValue(payrollPeriodKey, out var selectedAllowances)
                ? selectedAllowances
                : [];
            var monthlySummary = new PayrollPeriodSummaryDto(
                monthlyCalculation.Period,
                selectedDays,
                [.. monthlyAllowances.Select(static allowance => new MonthlyAllowanceDto(
                    allowance.Id,
                    allowance.DisplayName,
                    allowance.Amount))],
                monthlyCalculation.BasePaySubtotal,
                monthlyCalculation.PremiumSubtotal,
                monthlyCalculation.CountBonusSubtotal,
                monthlyCalculation.AllowanceSubtotal,
                monthlyCalculation.CalculatedSubtotal,
                monthlyCalculation.UncalculatedCount);
            var annualCalculation = annualCalculator.Aggregate(periodCalculations);
            var annualSummary = new AnnualSalarySummaryDto(
                annualRange.Start,
                annualRange.End,
                annualRange.AccumulationEnd,
                annualCalculation.CalculatedSubtotal,
                annualCalculation.UncalculatedCount);
            return new HomeSalarySummaryDto(monthlySummary, annualSummary);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<PayrollPeriodKey> GetPeriodKeys(
        PayrollPeriodKey start,
        PayrollPeriodKey end)
    {
        var values = new List<PayrollPeriodKey>();
        for (var current = start;; current = new PayrollPeriodKey(current.Value.AddMonths(1)))
        {
            values.Add(current);
            if (current == end)
            {
                return values;
            }
        }
    }

    private async Task<SalaryCalculationContext> LoadCalculationContextAsync(
        IEnumerable<DateOnly> dates,
        CancellationToken cancellationToken)
    {
        var months = dates.Select(ApplicationSupport.ToYearMonth).Distinct().OrderBy(x => x).ToArray();
        if (months.Length == 0) return SalaryCalculationContext.Empty;
        var snapshots = await settings.GetEffectiveForMonthsAsync(months, cancellationToken).ConfigureAwait(false);
        var versionIds = snapshots.Values.Select(x => x.HolidayCalendarVersionId).Distinct().ToArray();
        var calendars = await holidays.GetManyAsync(versionIds, cancellationToken).ConfigureAwait(false);
        return new(snapshots, calendars);
    }

    private DailySalaryDto CalculateDay(
        DateOnly date,
        IReadOnlyList<WorkRecordDto> values,
        SalaryCalculationContext context)
    {
        if (values.Count == 0)
        {
            var empty = calculator.AggregateDay(date, []);
            return new(date, [], empty.BasePaySubtotal, empty.PremiumSubtotal, empty.CountBonusSubtotal,
                empty.CalculatedSubtotal, empty.UncalculatedCount);
        }

        var settingMonth = ApplicationSupport.ToYearMonth(date);
        var snapshot = context.Snapshots[settingMonth];
        var holiday = context.HolidayCalendars[snapshot.HolidayCalendarVersionId];
        var calculationSnapshot = ApplicationSupport.ForCalculationDate(snapshot, date, holiday);
        var calculated = values.Select(value => calculator.Calculate(
            new WorkSalaryCalculationRequest(ApplicationSupport.ToDomain(value),
                calculationSnapshot, holiday))).ToArray();
        var aggregate = calculator.AggregateDay(date, calculated);
        var serviceNames = snapshot.Services.ToDictionary(x => x.Id, x => x.DisplayName);
        var categoryNames = snapshot.TimeCategories.ToDictionary(x => x.Id, x => x.DisplayName);
        return new(date, [.. values.Zip(calculated, (value, result) => new WorkRecordSalaryDto(
                value,
                result,
                serviceNames.GetValueOrDefault(value.ServiceId),
                value.TimeCategoryId is { } categoryId ? categoryNames.GetValueOrDefault(categoryId) : null,
                settingMonth))],
            aggregate.BasePaySubtotal, aggregate.PremiumSubtotal, aggregate.CountBonusSubtotal,
            aggregate.CalculatedSubtotal, aggregate.UncalculatedCount);
    }

    private static Task<T> RunCalculationAsync<T>(Func<T> calculation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(calculation, cancellationToken);
    }

    private static DailySalaryCalculation ToDomain(DailySalaryDto value)
    {
        return new(value.Date,
        [.. value.Records.Select(x => x.Calculation)], value.BasePaySubtotal, value.PremiumSubtotal,
        value.CountBonusSubtotal, value.CalculatedSubtotal, value.UncalculatedCount);
    }

    private sealed record SalaryCalculationContext(
        IReadOnlyDictionary<YearMonth, SettingSnapshot> Snapshots,
        IReadOnlyDictionary<HolidayCalendarVersionId, HolidayCalendar> HolidayCalendars)
    {
        public static SalaryCalculationContext Empty { get; } = new(
            new Dictionary<YearMonth, SettingSnapshot>(),
            new Dictionary<HolidayCalendarVersionId, HolidayCalendar>());
    }

}
