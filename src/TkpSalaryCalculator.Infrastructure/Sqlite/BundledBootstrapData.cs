namespace TkpSalaryCalculator.Infrastructure.Sqlite;

/// <summary>
/// Schema-v1 bootstrap data. IDs are stable so the seed can be applied repeatedly without
/// creating duplicate logical definitions.
/// </summary>
internal static class BundledBootstrapData
{
    internal const string HolidayCalendarId = "10000000-0000-4000-8000-000000000001";
    internal const string InitialSnapshotId = "10000000-0000-4000-8000-000000000002";
    internal const string PhysicalCareServiceId = "10000000-0000-4000-8000-000000000010";
    internal const string LivingSupportServiceId = "10000000-0000-4000-8000-000000000011";
    internal const string PhysicalZeroCategoryId = "10000000-0000-4000-8000-000000000020";
    internal const string PhysicalOneCategoryId = "10000000-0000-4000-8000-000000000021";
    internal const string PhysicalTwoCategoryId = "10000000-0000-4000-8000-000000000022";
    internal const string LivingTwoCategoryId = "10000000-0000-4000-8000-000000000023";
    internal const string LivingThreeCategoryId = "10000000-0000-4000-8000-000000000024";
    internal const string HolidayPremiumId = "10000000-0000-4000-8000-000000000030";

    internal const string HolidayVersionName = "cao-jp-holidays-2026-2027-20260816-v1";
    internal const string HolidaySourceName = "内閣府『国民の祝日について』公式CSV";
    internal const string HolidaySourceReferenceDate = "2026-08-16";

    internal static readonly (string Id, string Name, string ServiceId, string CategoryId, int Minutes, int Order)[]
        Presets =
        [
            ("10000000-0000-4000-8000-000000000040", "身体0", PhysicalCareServiceId,
                PhysicalZeroCategoryId, 20, 0),
            ("10000000-0000-4000-8000-000000000041", "身体1", PhysicalCareServiceId,
                PhysicalOneCategoryId, 30, 1),
            ("10000000-0000-4000-8000-000000000042", "身体2", PhysicalCareServiceId,
                PhysicalTwoCategoryId, 60, 2),
            ("10000000-0000-4000-8000-000000000043", "生活2", LivingSupportServiceId,
                LivingTwoCategoryId, 45, 3),
            ("10000000-0000-4000-8000-000000000044", "生活3", LivingSupportServiceId,
                LivingThreeCategoryId, 60, 4),
        ];

    // Fixed copy of the Cabinet Office official list available on 2026-08-16.
    // Coverage is deliberately explicit: a later application update must add a new immutable version.
    internal static readonly (string Date, string Name)[] Holidays =
    [
        ("2026-01-01", "元日"),
        ("2026-01-12", "成人の日"),
        ("2026-02-11", "建国記念の日"),
        ("2026-02-23", "天皇誕生日"),
        ("2026-03-20", "春分の日"),
        ("2026-04-29", "昭和の日"),
        ("2026-05-03", "憲法記念日"),
        ("2026-05-04", "みどりの日"),
        ("2026-05-05", "こどもの日"),
        ("2026-05-06", "休日"),
        ("2026-07-20", "海の日"),
        ("2026-08-11", "山の日"),
        ("2026-09-21", "敬老の日"),
        ("2026-09-22", "休日"),
        ("2026-09-23", "秋分の日"),
        ("2026-10-12", "スポーツの日"),
        ("2026-11-03", "文化の日"),
        ("2026-11-23", "勤労感謝の日"),
        ("2027-01-01", "元日"),
        ("2027-01-11", "成人の日"),
        ("2027-02-11", "建国記念の日"),
        ("2027-02-23", "天皇誕生日"),
        ("2027-03-21", "春分の日"),
        ("2027-03-22", "休日"),
        ("2027-04-29", "昭和の日"),
        ("2027-05-03", "憲法記念日"),
        ("2027-05-04", "みどりの日"),
        ("2027-05-05", "こどもの日"),
        ("2027-07-19", "海の日"),
        ("2027-08-11", "山の日"),
        ("2027-09-20", "敬老の日"),
        ("2027-09-23", "秋分の日"),
        ("2027-10-11", "スポーツの日"),
        ("2027-11-03", "文化の日"),
        ("2027-11-23", "勤労感謝の日"),
    ];
}
