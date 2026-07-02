using System.Globalization;
using System.Windows.Data;

namespace DLVoiceLibrary.Converters;

/// <summary>文字列が空でなければtrue。ToolTipService.IsEnabled等のbool依存プロパティ向け。</summary>
public sealed class NotEmptyToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
