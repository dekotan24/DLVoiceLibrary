namespace DLVoiceLibrary.Core.Services;

/// <summary>
/// TagLibSharpを使ってID3v2(mp3)/Vorbis Comment(ogg/flac)/MP4 atom(m4a)/WAV RIFF INFO等の
/// タグを横断的に読み取る実装。ファイル欠損・破損・非対応フォーマット等、あらゆる失敗時に
/// 例外を外に漏らさず、ファイル名ベースのフォールバック値を返す。
/// </summary>
public sealed class TagLibTagReaderService : ITagReaderService
{
    public TrackTagInfo ReadTags(string filePath)
    {
        var fallbackTitle = Path.GetFileNameWithoutExtension(filePath);

        try
        {
            using var file = TagLib.File.Create(filePath);

            var tag = file.Tag;

            var title = tag?.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = fallbackTitle;
            }

            var artist = tag?.FirstPerformer ?? string.Empty;

            var trackNumber = tag is null ? 0 : (int)tag.Track;

            long durationMs = 0;
            try
            {
                durationMs = (long)file.Properties.Duration.TotalMilliseconds;
            }
            catch
            {
                durationMs = 0;
            }

            return new TrackTagInfo(title, artist, trackNumber, durationMs);
        }
        catch
        {
            return new TrackTagInfo(fallbackTitle, string.Empty, 0, 0);
        }
    }
}
