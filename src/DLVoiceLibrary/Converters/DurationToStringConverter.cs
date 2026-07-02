using System.Globalization;
using System.Windows.Data;

namespace DLVoiceLibrary.Converters;

public sealed class DurationToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan span) return string.Empty;
        return span.Hours > 0
            ? span.ToString(@"h\:mm\:ss")
            : span.ToString(@"m\:ss");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
