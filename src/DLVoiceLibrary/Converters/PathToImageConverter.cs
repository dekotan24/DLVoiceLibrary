using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace DLVoiceLibrary.Converters;

public sealed class PathToImageConverter : IValueConverter
{
    /// <param name="parameter">デコード幅(px)。未指定なら260(一覧カード向けの省メモリ)、"0"なら原寸デコード(詳細パネル・拡大表示向け)。</param>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var decodeWidth = 260;
        if (parameter is not null && int.TryParse(parameter.ToString(), out var w))
        {
            decodeWidth = w;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0)
            {
                image.DecodePixelWidth = decodeWidth;
            }
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
