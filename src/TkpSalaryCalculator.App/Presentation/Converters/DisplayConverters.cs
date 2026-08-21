using System.Globalization;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Converters;

public sealed class YenAmountConverter : IValueConverter
{
    private readonly JapaneseDisplayFormatter formatter = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is YenAmount amount ? formatter.Money(amount) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DateOnlyConverter : IValueConverter
{
    private readonly JapaneseDisplayFormatter formatter = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateOnly date ? formatter.Date(date) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class MinuteOfDayConverter : IValueConverter
{
    private readonly JapaneseDisplayFormatter formatter = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is MinuteOfDay time ? formatter.Time(time) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class WorkMinutesConverter : IValueConverter
{
    private readonly JapaneseDisplayFormatter formatter = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WorkMinutes duration ? formatter.Duration(duration) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
