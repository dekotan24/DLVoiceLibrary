using System.Globalization;
using System.Windows.Data;
using DLVoiceLibrary.Core.Models;

namespace DLVoiceLibrary.Converters;

public sealed class RepeatModeToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RepeatMode.One => "🔂 1曲",
        RepeatMode.All => "🔁 全曲",
        _ => "➡ オフ"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
