using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Tests;

public sealed class ModelInvariantReviewTests
{
    [Fact]
    public void EveryIdentifierRejectsGuidEmptyAndKeepsDeconstruction()
    {
        Action[] invalidConstructions =
        {
            () => _ = new WorkRecordId(Guid.Empty),
            () => _ = new ServiceId(Guid.Empty),
            () => _ = new TimeCategoryId(Guid.Empty),
            () => _ = new PremiumId(Guid.Empty),
            () => _ = new CountBonusId(Guid.Empty),
            () => _ = new SettingSnapshotId(Guid.Empty),
            () => _ = new ClosingRuleId(Guid.Empty),
            () => _ = new MonthlyAllowanceId(Guid.Empty),
            () => _ = new HolidayCalendarVersionId(Guid.Empty),
            () => _ = new ServicePresetId(Guid.Empty),
            () => _ = new BasicShiftId(Guid.Empty),
        };

        foreach (var construction in invalidConstructions)
        {
            Assert.Throws<ArgumentException>(construction);
        }

        var expected = Guid.NewGuid();
        new ServiceId(expected).Deconstruct(out var actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DisplayOrderAndSchemaVersionRejectInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayOrder(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SchemaVersion(0));
    }

    [Fact]
    public void UndefinedWorkAndPremiumEnumsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkRecord(
            new WorkRecordId(Guid.NewGuid()),
            new DateOnly(2026, 8, 15),
            TestData.ServiceId,
            null,
            (WorkInputMode)99,
            new WorkMinutes(30),
            null,
            null));

        Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotPremium(
            new PremiumId(Guid.NewGuid()),
            "割増",
            (PremiumCalculationType)99,
            null,
            new YenAmount(100),
            null,
            null,
            false,
            new HashSet<DayOfWeek>(),
            new HashSet<DateOnly>(),
            new HashSet<ServiceId>(),
            true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void ClosingDayOutsideRangeIsRejected(int closingDay)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestData.ClosingRule(2026, 8, closingDay));
    }

    [Fact]
    public void PremiumRequiresTheValueMatchingItsCalculationType()
    {
        Assert.Throws<ArgumentException>(() => Premium(
            PremiumCalculationType.Percentage,
            percentage: null,
            amount: null));
        Assert.Throws<ArgumentException>(() => Premium(
            PremiumCalculationType.Percentage,
            percentage: new BasisPoints(2500),
            amount: new YenAmount(100)));
        Assert.Throws<ArgumentException>(() => Premium(
            PremiumCalculationType.FixedPerHour,
            percentage: new BasisPoints(2500),
            amount: null));
        Assert.Throws<ArgumentException>(() => Premium(
            PremiumCalculationType.FixedPerRecord,
            percentage: null,
            amount: null));
    }

    [Fact]
    public void PremiumTimeRangeRequiresBothDifferentEndpoints()
    {
        Assert.Throws<ArgumentException>(() => Premium(
            PremiumCalculationType.FixedPerHour,
            amount: new YenAmount(100),
            start: new MinuteOfDay(22 * 60)));
        Assert.Throws<ArgumentException>(() => Premium(
            PremiumCalculationType.FixedPerHour,
            amount: new YenAmount(100),
            start: new MinuteOfDay(22 * 60),
            end: new MinuteOfDay(22 * 60)));
    }

    [Fact]
    public void DurationRejectsEndOnlyAndMismatchedEnd()
    {
        Assert.Throws<ArgumentException>(() => new WorkRecord(
            new WorkRecordId(Guid.NewGuid()),
            new DateOnly(2026, 8, 15),
            TestData.ServiceId,
            null,
            WorkInputMode.Duration,
            new WorkMinutes(30),
            null,
            new MinuteOfDay(30)));
        Assert.Throws<ArgumentException>(() => new WorkRecord(
            new WorkRecordId(Guid.NewGuid()),
            new DateOnly(2026, 8, 15),
            TestData.ServiceId,
            null,
            WorkInputMode.Duration,
            new WorkMinutes(30),
            new MinuteOfDay(0),
            new MinuteOfDay(31)));
    }

    [Fact]
    public void EqualTimeRangeEndpointsRepresentExactlyTwentyFourHours()
    {
        var record = new WorkRecord(
            new WorkRecordId(Guid.NewGuid()),
            new DateOnly(2026, 8, 15),
            TestData.ServiceId,
            null,
            WorkInputMode.TimeRange,
            new WorkMinutes(1440),
            new MinuteOfDay(9 * 60),
            new MinuteOfDay(9 * 60));

        Assert.Equal(1440, record.WorkMinutes.Value);
    }

    [Fact]
    public void SnapshotRejectsDuplicateChildIds()
    {
        var service = Service(TestData.ServiceId, "身体");
        Assert.Throws<ArgumentException>(() => Snapshot(
            services: new[] { service, service }));

        var category = Category(TestData.CategoryId, TestData.ServiceId, "30分");
        Assert.Throws<ArgumentException>(() => Snapshot(
            timeCategories: new[] { category, category }));

        var premium = TestData.FixedPerRecordPremium(100);
        Assert.Throws<ArgumentException>(() => Snapshot(
            premiums: new[] { premium, premium }));

        var bonus = TestData.CountBonus(100);
        Assert.Throws<ArgumentException>(() => Snapshot(
            bonuses: new[] { bonus, bonus }));
    }

    [Fact]
    public void SnapshotRejectsUnresolvedChildReferences()
    {
        var missingService = new ServiceId(Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => Snapshot(
            timeCategories: new[] { Category(TestData.CategoryId, missingService, "30分") }));

        var premium = Premium(
            PremiumCalculationType.FixedPerRecord,
            amount: new YenAmount(100),
            serviceIds: new HashSet<ServiceId> { missingService });
        Assert.Throws<ArgumentException>(() => Snapshot(premiums: new[] { premium }));

        var bonus = new SnapshotCountBonus(
            new CountBonusId(Guid.NewGuid()),
            "件数",
            new YenAmount(100),
            new HashSet<ServiceId> { missingService },
            true);
        Assert.Throws<ArgumentException>(() => Snapshot(bonuses: new[] { bonus }));
    }

    [Fact]
    public void SameTimeCategoryDisplayNameUnderDifferentServicesIsAllowed()
    {
        var secondServiceId = new ServiceId(Guid.NewGuid());
        var snapshot = Snapshot(
            services: new[]
            {
                Service(TestData.ServiceId, "身体"),
                Service(secondServiceId, "生活"),
            },
            timeCategories: new[]
            {
                Category(TestData.CategoryId, TestData.ServiceId, "30分"),
                Category(new TimeCategoryId(Guid.NewGuid()), secondServiceId, "30分"),
            });

        Assert.Equal(2, snapshot.TimeCategories.Count);
    }

    [Fact]
    public void SnapshotRequiresUtcCreationTime()
    {
        Assert.Throws<ArgumentException>(() => Snapshot(
            createdAtUtc: new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.FromHours(9))));
        var fixedUtc = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(fixedUtc, Snapshot(createdAtUtc: fixedUtc).CreatedAtUtc);
    }

    private static SnapshotPremium Premium(
        PremiumCalculationType type,
        BasisPoints? percentage = null,
        YenAmount? amount = null,
        MinuteOfDay? start = null,
        MinuteOfDay? end = null,
        IReadOnlySet<ServiceId>? serviceIds = null) =>
        new(
            new PremiumId(Guid.NewGuid()),
            "割増",
            type,
            percentage,
            amount,
            start,
            end,
            false,
            new HashSet<DayOfWeek>(),
            new HashSet<DateOnly>(),
            serviceIds ?? new HashSet<ServiceId>(),
            true);

    private static SettingSnapshot Snapshot(
        IReadOnlyList<SnapshotService>? services = null,
        IReadOnlyList<SnapshotTimeCategory>? timeCategories = null,
        IReadOnlyList<SnapshotRate>? rates = null,
        IReadOnlyList<SnapshotPremium>? premiums = null,
        IReadOnlyList<SnapshotCountBonus>? bonuses = null,
        DateTimeOffset? createdAtUtc = null) =>
        new(
            new SettingSnapshotId(Guid.NewGuid()),
            null,
            TestData.HolidayVersionId,
            new SchemaVersion(1),
            createdAtUtc ?? new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            services ?? new[] { Service(TestData.ServiceId, "身体") },
            timeCategories ?? Array.Empty<SnapshotTimeCategory>(),
            rates ?? Array.Empty<SnapshotRate>(),
            premiums ?? Array.Empty<SnapshotPremium>(),
            bonuses ?? Array.Empty<SnapshotCountBonus>());

    private static SnapshotService Service(ServiceId id, string name) =>
        new(id, name, new DisplayOrder(0), true);

    private static SnapshotTimeCategory Category(TimeCategoryId id, ServiceId serviceId, string name) =>
        new(id, serviceId, name, new WorkMinutes(30), new DisplayOrder(0), true);
}
