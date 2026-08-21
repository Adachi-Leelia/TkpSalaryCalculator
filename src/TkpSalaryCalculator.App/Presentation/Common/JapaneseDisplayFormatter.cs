using System.Globalization;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Common;

/// <summary>金額、日付、時刻、期間を日本語の同一規約で表示します。</summary>
public sealed class JapaneseDisplayFormatter
{
    private static readonly CultureInfo JapaneseCulture = CultureInfo.GetCultureInfo("ja-JP");

    public string Money(YenAmount amount) => $"{amount.Value.ToString("N0", JapaneseCulture)}円";

    public string Date(DateOnly date, bool includeWeekday = true) => includeWeekday
        ? $"{date.ToString("yyyy年M月d日", JapaneseCulture)}（{date.ToString("ddd", JapaneseCulture)}）"
        : date.ToString("yyyy年M月d日", JapaneseCulture);

    public string Month(YearMonth month) => $"{month.Year}年{month.Month}月";

    public string SettingsMonth(YearMonth month) => $"設定対象年月: {Month(month)}";

    public string Time(MinuteOfDay time) => $"{time.Value / 60:00}:{time.Value % 60:00}";

    public string Duration(WorkMinutes duration)
    {
        var hours = duration.Value / 60;
        var minutes = duration.Value % 60;
        return hours switch
        {
            0 => $"{minutes}分",
            _ when minutes == 0 => $"{hours}時間",
            _ => $"{hours}時間{minutes}分",
        };
    }

    public string PayrollPeriod(PayrollPeriod period) =>
        $"給与算定開始日: {Date(period.StartDate, false)}\n給与算定終了日: {Date(period.EndDate, false)}";
}
