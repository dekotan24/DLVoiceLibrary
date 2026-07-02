using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DLVoiceLibrary.Converters;

/// <summary>bool値がtrueならVisible。ConverterParameterに"Invert"を渡すと逆(falseなら表示)になる。
/// ※bool値をNullToVisibilityConverterに通すとboolは常に非nullのため常時表示になる誤用バグを踏む。
/// bool値の表示切り替えには必ずこちらを使うこと。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
