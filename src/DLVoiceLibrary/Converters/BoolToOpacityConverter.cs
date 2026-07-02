using System.Globalization;
using System.Windows.Data;

namespace DLVoiceLibrary.Converters;

/// <summary>trueなら不透明(1.0)、falseなら半透明(0.4)。トグルボタンの有効状態を視覚的に示すため。</summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.4;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
