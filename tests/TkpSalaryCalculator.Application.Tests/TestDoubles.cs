using System.Runtime.CompilerServices;

namespace TkpSalaryCalculator.Application.Tests;

internal static class TestData
{
    public static readonly ServiceId ServiceId = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));
    public static readonly TimeCategoryId CategoryId = new(Guid.Parse("20000000-0000-0000-0000-000000000001"));
    public static readonly HolidayCalendarVersionId HolidayId = new(Guid.Parse("30000000-0000-0000-0000-000000000001"));

    public static SettingSnapshot Snapshot(YearMonth? month = null, bool serviceEnabled = true,
        bool categoryEnabled = true, IReadOnlyList<SnapshotPremium>? premiums = null,
        IReadOnlyList<SnapshotCountBonus>? bonuses = null,
        HolidayCalendarVersionId? holidayCalendarVersionId = null)
    {
        return new(
        new SettingSnapshotId(Guid.NewGuid()), null, holidayCalendarVersionId ?? HolidayId,
        new SchemaVersion(1), DateTimeOffset.UnixEpoch,
        [new SnapshotService(ServiceId, "訪問", new DisplayOrder(0), serviceEnabled)],
        [new SnapshotTimeCategory(CategoryId, ServiceId, "60分", new WorkMinutes(60), new DisplayOrder(0), categoryEnabled)],
        [new SnapshotRate(ServiceId, CategoryId, RateType.FixedPerRecord, new YenAmount(1000)),
         new SnapshotRate(ServiceId, null, RateType.Hourly, new YenAmount(1200))],
        premiums ?? [], bonuses ?? []);
    }


    public static WorkRecordDto Work(DateOnly date, WorkRecordId? id = null, BasicShiftId? shiftId = null,
        WorkMinutes? minutes = null)
    {
        return new(id ?? new WorkRecordId(Guid.NewGuid()), date, ServiceId, CategoryId,
        WorkInputMode.Duration, minutes ?? new WorkMinutes(60), null, null, null, shiftId, null);
    }


    public static SaveWorkRecordCommand SaveCommand(DateOnly date, WorkMinutes? minutes = null,
        MinuteOfDay? start = null, WorkInputMode mode = WorkInputMode.Duration, MinuteOfDay? end = null)
    {
        return new(null, date, ServiceId, CategoryId, mode,
            mode == WorkInputMode.Duration ? minutes ?? new WorkMinutes(60) : null, start, end, null, Guid.NewGuid());
    }

}

internal sealed class FakeClock(DateTimeOffset now) : IUtcClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

internal sealed class FakeLocalDateConverter(TimeSpan offset) : ILocalDateConverter
{
    public DateOnly ToLocalDate(DateTimeOffset utcDateTime)
    {
        return DateOnly.FromDateTime(utcDateTime.ToOffset(offset).DateTime);
    }

}

internal sealed class RecordingSalaryCalculator : ISalaryCalculator
{
    private readonly SalaryCalculator inner = new();
    public List<WorkSalaryCalculationRequest> Requests { get; } = [];
    public List<SynchronizationContext?> ExecutionContexts { get; } = [];

    public WorkSalaryCalculation Calculate(WorkSalaryCalculationRequest request)
    {
        ExecutionContexts.Add(SynchronizationContext.Current);
        Requests.Add(request);
        return inner.Calculate(request);
    }

    public DailySalaryCalculation AggregateDay(
        DateOnly workDate, IReadOnlyList<WorkSalaryCalculation> records)
    {
        ExecutionContexts.Add(SynchronizationContext.Current);
        return inner.AggregateDay(workDate, records);
    }

    public PayrollPeriodSalaryCalculation AggregatePeriod(
        PayrollPeriod period,
        IReadOnlyList<DailySalaryCalculation> days,
        IReadOnlyList<MonthlyAllowance> allowances)
    {
        ExecutionContexts.Add(SynchronizationContext.Current);
        return inner.AggregatePeriod(period, days, allowances);
    }
}

internal interface ITransactionalFakeState
{
    object CaptureState();
    void RestoreState(object snapshot);
}

internal sealed class FakeTransactionRunner : ITransactionRunner
{
    private readonly List<ITransactionalFakeState> participants = [];
    public int Calls { get; private set; }
    public int Commits { get; private set; }
    public void Register(params ITransactionalFakeState[] values)
    {
        participants.AddRange(values);
    }


    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        Calls++;
        var snapshots = participants.Select(x => (Participant: x, State: x.CaptureState())).ToArray();
        try
        {
            await operation(cancellationToken);
            Commits++;
        }
        catch
        {
            foreach (var (Participant, State) in snapshots.Reverse()) Participant.RestoreState(State);
            throw;
        }
    }
    public async Task<TResult> ExecuteAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        Calls++;
        var snapshots = participants.Select(x => (Participant: x, State: x.CaptureState())).ToArray();
        try
        {
            var result = await operation(cancellationToken);
            Commits++;
            return result;
        }
        catch
        {
            foreach (var (Participant, State) in snapshots.Reverse()) Participant.RestoreState(State);
            throw;
        }
    }
}

internal sealed class FakeMetadataRepository : IAppMetadataRepository, ITransactionalFakeState
{
    public AppMetadata Value { get; set; } = new(InitialSetupStatus.NotStarted, null, null, 1, null, null, null);
    public Exception? SetLastDataChangedFailure { get; set; }
    public Task<AppMetadata> GetAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Value);
    }


    public Task SetInitialSetupAsync(InitialSetupStatus status, string? step, SettingSnapshotId? initialSnapshotId, CancellationToken cancellationToken)
    { Value = Value with { InitialSetupStatus = status, InitialSetupStep = step, InitialSnapshotId = initialSnapshotId }; return Task.CompletedTask; }
    public Task SetExportFormatVersionAsync(int exportFormatVersion, CancellationToken cancellationToken)
    { Value = Value with { ExportFormatVersion = exportFormatVersion }; return Task.CompletedTask; }
    public Task SetLastDataChangedAtUtcAsync(DateTimeOffset changedAtUtc, CancellationToken cancellationToken)
    {
        if (SetLastDataChangedFailure is not null) throw SetLastDataChangedFailure;
        Value = Value with { LastDataChangedAtUtc = changedAtUtc };
        return Task.CompletedTask;
    }
    public Task SetLastExportedAtUtcAsync(DateTimeOffset exportedAtUtc, CancellationToken cancellationToken)
    { Value = Value with { LastExportedAtUtc = exportedAtUtc }; return Task.CompletedTask; }
    public Task SetBackupReminderDeferredUntilDateAsync(DateOnly? deferredUntilDate, CancellationToken cancellationToken)
    { Value = Value with { BackupReminderDeferredUntilDate = deferredUntilDate }; return Task.CompletedTask; }
    public object CaptureState()
    {
        return Value;
    }


    public void RestoreState(object snapshot)
    {
        Value = (AppMetadata)snapshot;
    }

}

internal sealed class FakeWorkRepository : IWorkRecordRepository, ITransactionalFakeState
{
    private readonly Dictionary<Guid, WorkRecordId> operations = [];
    public List<WorkRecordDto> Values { get; } = [];
    public Exception? UpsertFailure { get; set; }
    public int? UpsertFailureAtCall { get; set; }
    public TaskCompletionSource? UpsertGate { get; set; }
    public int UpsertCalls { get; private set; }
    public int FindCalls { get; private set; }
    public int StreamRangeCalls { get; private set; }
    public List<(DateOnly Start, DateOnly End)> StreamRanges { get; } = [];
    public Task<bool> AnyAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Values.Count != 0);
    }

    public Task<WorkRecordDto?> FindAsync(WorkRecordId id, CancellationToken cancellationToken)
    {
        FindCalls++;
        return Task.FromResult(Values.FirstOrDefault(x => x.Id == id));
    }

    public Task<WorkRecordDto?> FindBySaveOperationIdAsync(Guid operationId, CancellationToken cancellationToken)
    {
        return Task.FromResult(operations.TryGetValue(operationId, out var id) ? Values.FirstOrDefault(x => x.Id == id) : null);
    }


    public async IAsyncEnumerable<WorkRecordDto> StreamRangeAsync(DateOnly startDate, DateOnly endDate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        StreamRangeCalls++;
        StreamRanges.Add((startDate, endDate));
        foreach (var value in Values.Where(x => x.WorkDate >= startDate && x.WorkDate <= endDate).OrderBy(x => x.WorkDate).ThenBy(x => x.Id.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return value;
            await Task.Yield();
        }
    }
    public async Task UpsertAsync(WorkRecordDto workRecord, CancellationToken cancellationToken)
    {
        UpsertCalls++;
        if (UpsertGate is not null) await UpsertGate.Task.WaitAsync(cancellationToken);
        if (UpsertFailure is not null && (UpsertFailureAtCall is null || UpsertFailureAtCall == UpsertCalls)) throw UpsertFailure;
        var index = Values.FindIndex(x => x.Id == workRecord.Id);
        if (index < 0) Values.Add(workRecord); else Values[index] = workRecord;
    }
    public async Task<bool> TryInsertAsync(WorkRecordDto workRecord, Guid operationId, CancellationToken cancellationToken)
    {
        UpsertCalls++;
        if (UpsertGate is not null) await UpsertGate.Task.WaitAsync(cancellationToken);
        if (UpsertFailure is not null && (UpsertFailureAtCall is null || UpsertFailureAtCall == UpsertCalls)) throw UpsertFailure;
        lock (operations)
        {
            if (operations.ContainsKey(operationId)) return false;
            operations[operationId] = workRecord.Id;
            Values.Add(workRecord);
            return true;
        }
    }
    public Task DeleteAsync(WorkRecordId id, CancellationToken cancellationToken) { Values.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
    public object CaptureState()
    {
        return (Values.ToArray(), operations.ToDictionary(x => x.Key, x => x.Value));
    }


    public void RestoreState(object snapshot)
    {
        var state = ((WorkRecordDto[] Values, Dictionary<Guid, WorkRecordId> Operations))snapshot;
        Values.Clear(); Values.AddRange(state.Values);
        operations.Clear(); foreach (var value in state.Operations) operations.Add(value.Key, value.Value);
    }
}

internal sealed class FakeSettingRepository : ISettingSnapshotRepository, ITransactionalFakeState
{
    public Dictionary<YearMonth, SettingSnapshot> Months { get; } = [];
    public SettingSnapshot Fallback { get; set; } = TestData.Snapshot();
    public int EnsureCalls { get; private set; }
    public int TryEnsureCalls { get; private set; }
    public int CloneCalls { get; private set; }
    public int EffectiveMonthCalls { get; private set; }
    public int EffectiveMonthsBatchCalls { get; private set; }
    public List<YearMonth> EffectiveMonthRequests { get; } = [];
    public Exception? CloneFailure { get; set; }
    public bool ForceCasFailure { get; set; }
    public Task<SettingSnapshot?> FindAsync(SettingSnapshotId id, CancellationToken cancellationToken)
    {
        return Task.FromResult(Months.Values.Append(Fallback).FirstOrDefault(x => x.Id == id));
    }

    public Task<SettingSnapshot?> FindForMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken)
    {
        return Task.FromResult(Months.TryGetValue(yearMonth, out var value) ? value : null);
    }

    public Task<SettingSnapshot> GetEffectiveForMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken)
    {
        EffectiveMonthCalls++;
        EffectiveMonthRequests.Add(yearMonth);
        return Task.FromResult(GetEffective(yearMonth));
    }

    public Task<IReadOnlyDictionary<YearMonth, SettingSnapshot>> GetEffectiveForMonthsAsync(
        IReadOnlyCollection<YearMonth> yearMonths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(yearMonths);
        cancellationToken.ThrowIfCancellationRequested();
        EffectiveMonthsBatchCalls++;
        var requested = yearMonths.Distinct().ToArray();
        EffectiveMonthRequests.AddRange(requested);
        return Task.FromResult<IReadOnlyDictionary<YearMonth, SettingSnapshot>>(
            requested.ToDictionary(x => x, GetEffective));
    }

    private SettingSnapshot GetEffective(YearMonth yearMonth) =>
        Months.Where(x => x.Key.CompareTo(yearMonth) <= 0).OrderBy(x => x.Key)
            .Select(x => x.Value).LastOrDefault() ?? Fallback;

    public Task<SettingSnapshot> EnsureForMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken)
    { EnsureCalls++; if (!Months.TryGetValue(yearMonth, out var value)) Months[yearMonth] = value = Fallback; return Task.FromResult(value); }
    public Task<SettingSnapshot?> TryEnsureForMonthAsync(YearMonth yearMonth,
        SettingSnapshotId expectedEffectiveSnapshotId, HolidayCalendarVersionId expectedHolidayCalendarVersionId,
        CancellationToken cancellationToken)
    {
        TryEnsureCalls++;
        if (Months.TryGetValue(yearMonth, out var existing))
            return Task.FromResult<SettingSnapshot?>(existing.Id == expectedEffectiveSnapshotId ? existing : null);
        var effective = Months.Where(x => x.Key.CompareTo(yearMonth) <= 0).OrderBy(x => x.Key)
            .Select(x => x.Value).LastOrDefault() ?? Fallback;
        if (effective.Id != expectedEffectiveSnapshotId) return Task.FromResult<SettingSnapshot?>(null);
        var selected = effective.HolidayCalendarVersionId == expectedHolidayCalendarVersionId
            ? effective
            : new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), effective.Id, expectedHolidayCalendarVersionId,
                effective.SchemaVersion, DateTimeOffset.UnixEpoch, effective.Services, effective.TimeCategories,
                effective.Rates, effective.Premiums, effective.CountBonuses);
        Months[yearMonth] = selected;
        return Task.FromResult<SettingSnapshot?>(selected);
    }
    public Task<SettingSnapshot?> TryCloneAndReplaceMonthSnapshotAsync(YearMonth yearMonth,
        SettingSnapshotId expectedCurrentSnapshotId, SettingSnapshotReplacementDto replacement,
        HolidayCalendarVersionId holidayCalendarVersionId, DateTimeOffset createdAtUtc, CancellationToken cancellationToken)
    {
        CloneCalls++;
        if (CloneFailure is not null) throw CloneFailure;
        if (ForceCasFailure) return Task.FromResult<SettingSnapshot?>(null);
        var current = Months.TryGetValue(yearMonth, out var value) ? value : Fallback;
        if (current.Id != expectedCurrentSnapshotId) return Task.FromResult<SettingSnapshot?>(null);
        var next = new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), current.Id, holidayCalendarVersionId,
            current.SchemaVersion, createdAtUtc, replacement.Services, replacement.TimeCategories, replacement.Rates,
            replacement.Premiums, replacement.CountBonuses);
        Months[yearMonth] = next;
        return Task.FromResult<SettingSnapshot?>(next);
    }
    public object CaptureState()
    {
        return (Months.ToDictionary(x => x.Key, x => x.Value), Fallback);
    }


    public void RestoreState(object snapshot)
    {
        var state = ((Dictionary<YearMonth, SettingSnapshot> Months, SettingSnapshot Fallback))snapshot;
        Months.Clear(); foreach (var value in state.Months) Months.Add(value.Key, value.Value);
        Fallback = state.Fallback;
    }
}

internal sealed class FakeHolidayRepository : IHolidayCalendarRepository
{
    public HolidayCalendarVersionId Latest { get; set; } = TestData.HolidayId;
    public Dictionary<HolidayCalendarVersionId, IReadOnlyDictionary<DateOnly, string>> Calendars { get; } = [];
    public int GetCalls { get; private set; }
    public int GetManyCalls { get; private set; }
    public List<HolidayCalendarVersionId> RequestedVersions { get; } = [];
    public Task<HolidayCalendar> GetAsync(HolidayCalendarVersionId versionId, CancellationToken cancellationToken)
    {
        GetCalls++;
        RequestedVersions.Add(versionId);
        return Task.FromResult(new HolidayCalendar(versionId,
            Calendars.GetValueOrDefault(versionId, new Dictionary<DateOnly, string>())));
    }

    public Task<IReadOnlyDictionary<HolidayCalendarVersionId, HolidayCalendar>> GetManyAsync(
        IReadOnlyCollection<HolidayCalendarVersionId> versionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(versionIds);
        cancellationToken.ThrowIfCancellationRequested();
        GetManyCalls++;
        var requested = versionIds.Distinct().ToArray();
        RequestedVersions.AddRange(requested);
        return Task.FromResult<IReadOnlyDictionary<HolidayCalendarVersionId, HolidayCalendar>>(
            requested.ToDictionary(x => x,
                x => new HolidayCalendar(x, Calendars.GetValueOrDefault(x, new Dictionary<DateOnly, string>()))));
    }

    public Task<HolidayCalendarVersionId> GetLatestVerifiedVersionIdAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Latest);
    }

}

internal sealed class FakePresetRepository : IServicePresetRepository, ITransactionalFakeState
{
    public List<ServicePresetDto> Values { get; } = [];
    public int GetAllCalls { get; private set; }
    public Task<IReadOnlyList<ServicePresetDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        GetAllCalls++;
        return Task.FromResult<IReadOnlyList<ServicePresetDto>>([.. Values]);
    }


    public Task UpsertAsync(ServicePresetDto preset, CancellationToken cancellationToken) { Values.RemoveAll(x => x.Id == preset.Id); Values.Add(preset); return Task.CompletedTask; }
    public Task DeleteAsync(ServicePresetId id, CancellationToken cancellationToken) { Values.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
    public object CaptureState()
    {
        return Values.ToArray();
    }


    public void RestoreState(object snapshot) { Values.Clear(); Values.AddRange((ServicePresetDto[])snapshot); }
}

internal sealed class FakeShiftRepository : IBasicShiftRepository, ITransactionalFakeState
{
    public List<BasicShiftDto> Values { get; } = [];
    public int GetForWeekdayCalls { get; private set; }
    public int GetForWeekdaysCalls { get; private set; }
    public List<DayOfWeek> RequestedWeekdays { get; } = [];
    public Task<IReadOnlyList<BasicShiftDto>> GetForWeekdayAsync(DayOfWeek weekday, CancellationToken cancellationToken)
    {
        GetForWeekdayCalls++;
        RequestedWeekdays.Add(weekday);
        return Task.FromResult<IReadOnlyList<BasicShiftDto>>([.. Values.Where(x => x.Weekday == weekday)]);
    }

    public Task<IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BasicShiftDto>>> GetForWeekdaysAsync(
        IReadOnlyCollection<DayOfWeek> weekdays, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(weekdays);
        cancellationToken.ThrowIfCancellationRequested();
        GetForWeekdaysCalls++;
        var requested = weekdays.Distinct().ToArray();
        RequestedWeekdays.AddRange(requested);
        return Task.FromResult<IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BasicShiftDto>>>(
            requested.ToDictionary(x => x,
                x => (IReadOnlyList<BasicShiftDto>)[.. Values.Where(value => value.Weekday == x)]));
    }

    public Task<BasicShiftDto?> FindAsync(BasicShiftId id, CancellationToken cancellationToken)
    {
        return Task.FromResult(Values.FirstOrDefault(x => x.Id == id));
    }


    public Task UpsertAsync(BasicShiftDto basicShift, CancellationToken cancellationToken) { Values.RemoveAll(x => x.Id == basicShift.Id); Values.Add(basicShift); return Task.CompletedTask; }
    public Task DeleteAsync(BasicShiftId id, CancellationToken cancellationToken) { Values.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
    public object CaptureState()
    {
        return Values.ToArray();
    }


    public void RestoreState(object snapshot) { Values.Clear(); Values.AddRange((BasicShiftDto[])snapshot); }
}

internal sealed class FakeClosingRepository : IClosingRuleRepository, ITransactionalFakeState
{
    public List<ClosingRule> Values { get; } = [];
    public int GetHistoryCalls { get; private set; }
    private int version;
    public bool ForceCasFailure { get; set; }
    public int ReplaceCalls { get; private set; }
    public Task<ClosingRuleHistorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new ClosingRuleHistorySnapshot([.. Values], new ClosingRuleHistoryVersion(version.ToString())));
    }

    public Task<IReadOnlyList<ClosingRule>> GetHistoryAsync(CancellationToken cancellationToken)
    {
        GetHistoryCalls++;
        return Task.FromResult<IReadOnlyList<ClosingRule>>([.. Values]);
    }


    public Task<bool> TryReplaceEffectiveRuleAsync(ClosingRule rule, ClosingRuleHistoryVersion expectedVersion, CancellationToken cancellationToken)
    {
        ReplaceCalls++;
        if (ForceCasFailure) return Task.FromResult(false);
        if (expectedVersion.Value != version.ToString()) return Task.FromResult(false);
        Values.RemoveAll(x => x.EffectiveFrom == rule.EffectiveFrom); Values.Add(rule); version++;
        return Task.FromResult(true);
    }
    public object CaptureState()
    {
        return (Values.ToArray(), version);
    }


    public void RestoreState(object snapshot)
    {
        var state = ((ClosingRule[] Values, int Version))snapshot;
        Values.Clear(); Values.AddRange(state.Values); version = state.Version;
    }
}

internal sealed class FakeAllowanceRepository : IMonthlyAllowanceRepository, ITransactionalFakeState
{
    public List<MonthlyAllowance> Values { get; } = [];
    public int GetForPeriodCalls { get; private set; }
    public int GetForRangeCalls { get; private set; }
    public Task<IReadOnlyList<MonthlyAllowance>> GetForPeriodAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken)
    {
        GetForPeriodCalls++;
        return Task.FromResult<IReadOnlyList<MonthlyAllowance>>([.. Values.Where(x => x.PayrollPeriodKey == payrollPeriodKey)]);
    }

    public Task<IReadOnlyList<MonthlyAllowance>> GetForRangeAsync(
        PayrollPeriodKey start,
        PayrollPeriodKey end,
        CancellationToken cancellationToken)
    {
        GetForRangeCalls++;
        return Task.FromResult<IReadOnlyList<MonthlyAllowance>>([.. Values
            .Where(x => x.PayrollPeriodKey.Value.CompareTo(start.Value) >= 0 &&
                        x.PayrollPeriodKey.Value.CompareTo(end.Value) <= 0)
            .OrderBy(x => x.PayrollPeriodKey.Value)]);
    }


    public Task UpsertAsync(MonthlyAllowance allowance, CancellationToken cancellationToken) { Values.RemoveAll(x => x.Id == allowance.Id); Values.Add(allowance); return Task.CompletedTask; }
    public Task DeleteAsync(MonthlyAllowanceId id, CancellationToken cancellationToken) { Values.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
    public object CaptureState()
    {
        return Values.ToArray();
    }


    public void RestoreState(object snapshot) { Values.Clear(); Values.AddRange((MonthlyAllowance[])snapshot); }
}

internal sealed class TestContext
{
    public FakeWorkRepository Works { get; } = new();
    public FakeSettingRepository Settings { get; } = new();
    public FakeHolidayRepository Holidays { get; } = new();
    public FakePresetRepository Presets { get; } = new();
    public FakeShiftRepository Shifts { get; } = new();
    public FakeClosingRepository Closing { get; } = new();
    public FakeAllowanceRepository Allowances { get; } = new();
    public FakeMetadataRepository Metadata { get; } = new();
    public FakeTransactionRunner Transactions { get; } = new();
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));
    public SalaryCalculator Salary { get; } = new();
    public PayrollPeriodCalculator Periods { get; } = new();

    public TestContext() => Transactions.Register(Works, Settings, Presets, Shifts, Closing, Allowances, Metadata);

    public WorkRecordUseCase WorkUseCase()
    {
        return new(Works, Settings, Presets, Holidays, Salary, Transactions, Metadata, Clock);
    }


    public BasicShiftUseCase ShiftUseCase()
    {
        return new(Shifts, Works, Settings, Holidays, Salary, Transactions, Metadata, Clock);
    }

}

internal sealed class FakeExportStream : IJsonExportStream
{
    public ExportDocumentHeader? Header { get; private set; }
    public int Count { get; private set; }
    public Exception? Failure { get; set; }
    public int? FailAfterRecord { get; set; }
    public async Task WriteAsync(Stream destination, ExportDocumentHeader header,
        IAsyncEnumerable<DataTransferRecord> records, CancellationToken cancellationToken)
    {
        Header = header;
        await destination.WriteAsync("{"u8.ToArray(), cancellationToken);
        await foreach (var _ in records.WithCancellation(cancellationToken))
        {
            Count++;
            await destination.WriteAsync("x"u8.ToArray(), cancellationToken);
            if (Failure is not null && (FailAfterRecord is null || Count >= FailAfterRecord)) throw Failure;
        }
        await destination.WriteAsync("}"u8.ToArray(), cancellationToken);
    }
}

internal sealed class FakeImportStream : IJsonImportStream
{
    public List<DataTransferRecord> Values { get; } = [];
    public Exception? Failure { get; set; }
    public Action? AfterFirstRecord { get; set; }
    public async IAsyncEnumerable<DataTransferRecord> ReadAsync(Stream source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Failure is not null) throw Failure;
        for (var index = 0; index < Values.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Values[index];
            if (index == 0) AfterFirstRecord?.Invoke();
            await Task.Yield();
        }
    }
}

internal sealed class FakeExportDataSource : IExportDataSource
{
    public List<DataTransferRecord> Values { get; } = [];
    public int OpenCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public Action? AfterFirstRecord { get; set; }
    public Task<IExportReadSession> OpenReadSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenCalls++;
        return Task.FromResult<IExportReadSession>(new Session([.. Values], this));
    }

    private sealed class Session(DataTransferRecord[] snapshot, FakeExportDataSource owner) : IExportReadSession
    {
        public async IAsyncEnumerable<DataTransferRecord> StreamAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var index = 0; index < snapshot.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return snapshot[index];
                if (index == 0) owner.AfterFirstRecord?.Invoke();
                await Task.Yield();
            }
        }
        public ValueTask DisposeAsync() { owner.DisposeCalls++; return ValueTask.CompletedTask; }
    }
}

internal enum FakeStagingState { Created, Validated, Consumed, Discarded }

internal sealed class FakeStagingRepository : IImportStagingRepository, ITransactionalFakeState
{
    public PreparedImportId Id { get; set; } = new(Guid.NewGuid());
    private readonly Dictionary<PreparedImportId, (FakeStagingState State, List<DataTransferRecord> Records)> entries = [];
    public List<DataTransferRecord> LiveData { get; } = [];
    public IReadOnlyList<DataTransferRecord> Values => entries.TryGetValue(Id, out var entry) ? entry.Records : [];
    public FakeStagingState? GetState(PreparedImportId id)
    {
        return entries.TryGetValue(id, out var entry) ? entry.State : null;
    }


    public int DiscardCalls { get; private set; }
    public int ReplaceCalls { get; private set; }
    public int AbandonedCalls { get; private set; }
    public Exception? DiscardFailure { get; set; }
    public bool ConsumeResult { get; set; } = true;
    public Exception? ConsumeFailureAfterReplacement { get; set; }
    public Task<PreparedImportId> CreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        entries[Id] = (FakeStagingState.Created, []);
        return Task.FromResult(Id);
    }
    public Task AppendBatchAsync(PreparedImportId preparedImportId, IReadOnlyList<DataTransferRecord> records, CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue(preparedImportId, out var entry) || entry.State != FakeStagingState.Created)
            throw new InvalidOperationException("staging is not writable");
        entry.Records.AddRange(records); return Task.CompletedTask;
    }
    public Task<ImportPreviewDto> ValidateAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue(preparedImportId, out var entry) || entry.State != FakeStagingState.Created)
            throw new InvalidOperationException("staging is not validatable");
        entries[preparedImportId] = (FakeStagingState.Validated, entry.Records);
        return Task.FromResult(new ImportPreviewDto(preparedImportId, 1, DateTimeOffset.UnixEpoch, 0, 0, entry.Records.Count, 0,
            null, null, null, null, []));
    }
    public Task<bool> TryConsumeAndReplaceLiveDataAsync(PreparedImportId preparedImportId, DateTimeOffset importedAtUtc, CancellationToken cancellationToken)
    {
        ReplaceCalls++;
        if (!ConsumeResult || !entries.TryGetValue(preparedImportId, out var entry) || entry.State != FakeStagingState.Validated)
            return Task.FromResult(false);
        var previousLiveData = LiveData.ToArray();
        LiveData.Clear(); LiveData.AddRange(entry.Records);
        entries[preparedImportId] = (FakeStagingState.Consumed, entry.Records);
        if (ConsumeFailureAfterReplacement is not null)
        {
            LiveData.Clear();
            LiveData.AddRange(previousLiveData);
            entries[preparedImportId] = (FakeStagingState.Validated, entry.Records);
            throw ConsumeFailureAfterReplacement;
        }
        return Task.FromResult(true);
    }
    public Task DiscardAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken)
    {
        DiscardCalls++;
        if (DiscardFailure is not null) return Task.FromException(DiscardFailure);

        if (entries.TryGetValue(preparedImportId, out _)) entries[preparedImportId] = (FakeStagingState.Discarded, []);
        return Task.CompletedTask;
    }
    public Task DiscardAbandonedAsync(CancellationToken cancellationToken)
    {
        AbandonedCalls++;
        foreach (var value in entries.Where(x => x.Value.State is FakeStagingState.Created or FakeStagingState.Validated).ToArray())
            entries[value.Key] = (FakeStagingState.Discarded, []);
        return Task.CompletedTask;
    }
    public object CaptureState()
    {
        return (entries.ToDictionary(x => x.Key,
        x => (x.Value.State, x.Value.Records.ToList())), LiveData.ToArray());
    }


    public void RestoreState(object snapshot)
    {
        var state = ((Dictionary<PreparedImportId, (FakeStagingState State, List<DataTransferRecord> Records)> Entries,
            DataTransferRecord[] LiveData))snapshot;
        entries.Clear(); foreach (var value in state.Entries) entries.Add(value.Key, value.Value);
        LiveData.Clear(); LiveData.AddRange(state.LiveData);
    }
}
