namespace DLVoiceLibrary.Core.Services;

/// <summary>音声ファイル1件からタグ情報を読み取る。タグ欠損・破損時はファイル名等へのフォールバックを行い、例外は投げない。</summary>
public interface ITagReaderService
{
    TrackTagInfo ReadTags(string filePath);
}

/// <summary>Title/Artistが空文字の場合、呼び出し側(FolderScanService)は既定値を必要としない —
/// ITagReaderServiceの実装が必ずファイル名ベースのフォールバックを済ませた値を返す。</summary>
public sealed record TrackTagInfo(string Title, string Artist, int TrackNumber, long DurationMs);
