using System.Globalization;
using System.Windows.Data;

namespace DLVoiceLibrary.Converters;

/// <summary>trueなら「一時停止中」ボタンに表示するべき「再開」文言、falseなら「一時停止」文言。</summary>
public sealed class BoolToPauseResumeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "▶ 再開" : "⏸ 一時停止";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
