using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DLVoiceLibrary.Converters;

/// <summary>valueのToString()がConverterParameter(文字列)と一致すればVisible、それ以外はCollapsed。タブ切り替え用。</summary>
public sealed class EnumEqualsVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return Visibility.Collapsed;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
