namespace DLVoiceLibrary.Core.Services;

public interface IFolderScanService
{
    /// <summary>フォルダ名からDLsite/FANZA系の作品ID(RJ/VJ/BJ/RG/RE + 6〜8桁)を抽出する。見つからなければnull。</summary>
    ProductIdMatch? ExtractProductId(string folderName);

    /// <summary>
    /// 作品フォルダ配下を再帰的に走査し、対応拡張子(mp3/wav/flac/ogg/m4a、大文字小文字無視)の音声ファイルを
    /// 列挙する。TrackNoはファイル名のタグではなく、フォルダ階層を深さ優先・自然順(数値部分を数値として比較)で
    /// 辿った順に1から採番する(DLsite音声作品はID3のトラック番号が欠損・不整合であることが多いため)。
    /// </summary>
    List<ScannedTrack> ScanTracks(string workFolderPath);
}

public sealed record ProductIdMatch(string ProductId, string Source);

public sealed record ScannedTrack(string FilePath, int TrackNo, string Title, string Artist, long DurationMs, string FileFormat);
