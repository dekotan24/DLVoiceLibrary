using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DLVoiceLibrary.Converters;

/// <summary>値がnull(文字列の場合は空文字・空白のみも含む)なら非表示。
/// ConverterParameterに"Invert"を渡すと逆(nullなら表示)になる。</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNull = value is null || (value is string s && string.IsNullOrWhiteSpace(s));
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var visible = invert ? isNull : !isNull;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
