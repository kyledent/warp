using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WarpAvalonia.Controls;

/// <summary>
/// Converts between decimal (WarpLib property type) and double (Avalonia slider type).
/// WarpLib uses decimal for pixel sizes, voltages, etc. Avalonia controls use double.
/// </summary>
public class DecimalDoubleConverter : IValueConverter
{
    public static readonly DecimalDoubleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is decimal d)
            return (double)d;
        if (value is int i)
            return (double)i;
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            if (targetType == typeof(decimal))
                return (decimal)d;
            if (targetType == typeof(int))
                return (int)Math.Round(d);
        }
        return value;
    }
}
