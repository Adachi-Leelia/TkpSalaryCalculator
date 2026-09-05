using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Domain.ValueObjects;
using System.Xml.Linq;

namespace TkpSalaryCalculator.Domain.Tests;

public sealed class DomainInvariantTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void WorkMinutes_OutsideRangeIsRejected(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkMinutes(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1440)]
    public void MinuteOfDay_OutsideRangeIsRejected(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MinuteOfDay(value));
    }

    [Fact]
    public void NegativeMoneyAndPercentageAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new YenAmount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BasisPoints(-1));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10000, 1)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public void InvalidYearMonthIsRejected(int year, int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new YearMonth(year, month));
    }

    [Fact]
    public void DefaultStructValuesAreRejectedAtModelBoundary()
    {
        Assert.Throws<ArgumentException>(() => new PayrollPeriodKey(default));
        Assert.Throws<ArgumentException>(() => TestData.CreateWorkRecord(
            default,
            new DateOnly(2026, 8, 15),
            TestData.ServiceId,
            null,
            WorkInputMode.Duration,
            default,
            null,
            null));
    }

    [Fact]
    public void UndefinedEnumIsRejectedAtModelBoundary()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotRate(
            TestData.ServiceId,
            null,
            (RateType)99,
            new YenAmount(1000)));
    }

    [Fact]
    public void BlankDisplayNameAndNullCollectionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new SnapshotService(
            TestData.ServiceId,
            "  ",
            new DisplayOrder(0),
            true));
        Assert.Throws<ArgumentNullException>(() => new SnapshotCountBonus(
            new CountBonusId(Guid.NewGuid()),
            "件数加算",
            new YenAmount(100),
            null!,
            true));
    }

    [Fact]
    public void TimeRangeMustMatchWorkMinutes()
    {
        Assert.Throws<ArgumentException>(() => TestData.CreateWorkRecord(
            new WorkRecordId(Guid.NewGuid()),
            new DateOnly(2026, 8, 15),
            TestData.ServiceId,
            null,
            WorkInputMode.TimeRange,
            new WorkMinutes(30),
            new MinuteOfDay(22 * 60),
            new MinuteOfDay(23 * 60)));
    }

    [Fact]
    public void MissingIntervalForApplicableTimePremiumIsRejected()
    {
        var snapshot = TestData.Snapshot(
            TestData.Rate(RateType.Hourly, 1200),
            [TestData.FixedPerHourPremium(300, 22 * 60, 5 * 60)]);

        Assert.Throws<ArgumentException>(() => new SalaryCalculator().Calculate(
            TestData.Request(TestData.WorkRecord(60), snapshot)));
    }

    [Fact]
    public void SnapshotCopiesCollectionsDefensively()
    {
        var services = new List<SnapshotService>
        {
            new(TestData.ServiceId, "身体", new DisplayOrder(0), true),
        };
        var snapshot = new SettingSnapshot(
            new SettingSnapshotId(Guid.NewGuid()),
            null,
            TestData.HolidayVersionId,
            new SchemaVersion(1),
            DateTimeOffset.Parse("2026-08-15T00:00:00Z"),
            services,
            [],
            [TestData.Rate(RateType.Hourly, 1200)],
            [],
            []);

        services.Clear();

        Assert.Single(snapshot.Services);
        Assert.Throws<NotSupportedException>(() => ((IList<SnapshotService>)snapshot.Services).Clear());
    }

    [Fact]
    public void SnapshotRejectsDuplicateRatesAndInvalidReferences()
    {
        Assert.Throws<ArgumentException>(() => TestData.Snapshot(
            TestData.Rate(RateType.Hourly, 1200),
            additionalRates: [TestData.Rate(RateType.FixedPerRecord, 850)]));

        var unrelatedService = new ServiceId(Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => new SettingSnapshot(
            new SettingSnapshotId(Guid.NewGuid()),
            null,
            TestData.HolidayVersionId,
            new SchemaVersion(1),
            DateTimeOffset.UtcNow,
            [new SnapshotService(TestData.ServiceId, "身体", new DisplayOrder(0), true)],
            [],
            [new SnapshotRate(unrelatedService, null, RateType.Hourly, new YenAmount(1000))],
            [],
            []));
    }

    [Fact]
    public void HolidayCalendarAndRuleSetsAreDefensivelyCopied()
    {
        var services = new HashSet<ServiceId> { TestData.ServiceId };
        var premium = TestData.FixedPerRecordPremium(100, services: services);
        var holidays = new Dictionary<DateOnly, string> { [new DateOnly(2026, 8, 15)] = "祝日" };
        var calendar = new HolidayCalendar(TestData.HolidayVersionId, holidays);

        services.Clear();
        holidays.Clear();

        Assert.Contains(TestData.ServiceId, premium.ServiceIds);
        Assert.Single(calendar.Holidays);
    }

    [Fact]
    public void ResultCollectionsCannotBeMutated()
    {
        var premium = TestData.FixedPerRecordPremium(100);
        var result = new SalaryCalculator().Calculate(TestData.Request(
            TestData.WorkRecord(30),
            TestData.Snapshot(TestData.Rate(RateType.FixedPerRecord, 100), [premium])));

        Assert.Throws<NotSupportedException>(() => ((IList<AppliedPremium>)result.Premiums).Clear());
    }

    [Fact]
    public void MonetaryMultiplicationOverflowIsDetected()
    {
        var snapshot = TestData.Snapshot(TestData.Rate(RateType.Hourly, long.MaxValue));

        Assert.Throws<OverflowException>(() => new SalaryCalculator().Calculate(
            TestData.Request(TestData.WorkRecord(1440), snapshot)));
    }

    [Fact]
    public void MonetaryAggregationOverflowIsDetected()
    {
        var calculator = new SalaryCalculator();
        var records = new[]
        {
            TestData.CalculatedRecord(long.MaxValue),
            TestData.CalculatedRecord(long.MaxValue, Guid.NewGuid()),
        };

        Assert.Throws<OverflowException>(() => calculator.AggregateDay(new DateOnly(2026, 8, 15), records));
    }

    [Fact]
    public void AggregatePeriodRejectsOutOfPeriodDayAndAllowance()
    {
        var calculator = new SalaryCalculator();
        var period = TestData.Period(2026, 8, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 20));
        var day = calculator.AggregateDay(
            new DateOnly(2026, 8, 21),
            [TestData.CalculatedRecord(100)]);
        Assert.Throws<ArgumentException>(() => calculator.AggregatePeriod(period, [day], []));

        var allowance = new MonthlyAllowance(
            new MonthlyAllowanceId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(2026, 9)),
            "手当",
            new YenAmount(100));
        Assert.Throws<ArgumentException>(() => calculator.AggregatePeriod(period, [], [allowance]));
    }

    [Fact]
    public void Arch001DomainProjectHasOnlyAllowedFrameworkReferences()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "TkpSalaryCalculator.Domain",
            "TkpSalaryCalculator.Domain.csproj");
        var project = XDocument.Load(projectPath);
        Assert.Empty(project.Descendants("ProjectReference"));
        Assert.Empty(project.Descendants("PackageReference"));

        var references = typeof(SalaryCalculator).Assembly.GetReferencedAssemblies();
        var allowedReferences = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Collections",
            "System.Collections.Immutable",
            "System.Linq",
            "System.Runtime",
            "System.Runtime.Numerics",
        };

        Assert.All(references, reference => Assert.Contains(reference.Name!, allowedReferences));
        Assert.DoesNotContain(references, reference =>
            reference.Name?.Contains("Json", StringComparison.OrdinalIgnoreCase) == true);

        var domainRoot = Path.GetDirectoryName(projectPath)!;
        foreach (var sourceFile in Directory.EnumerateFiles(domainRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                                    !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain("System.Text.Json", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Newtonsoft.Json", source, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TkpSalaryCalculator.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("リポジトリルートを特定できませんでした。");
    }
}
