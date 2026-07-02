namespace DLVoiceLibrary.Scraping;

public interface IDlsiteScraperService
{
    /// <summary>
    /// DLsiteの商品ページを取得して作品メタデータを解析する。ネットワークエラー・404・パース失敗等、
    /// あらゆる失敗時には例外を投げずnullを返す(呼び出し側はnullを「取得できなかった」として扱う)。
    /// </summary>
    Task<DlsiteWorkMetadata?> FetchAsync(string productId, CancellationToken ct = default);
}

public sealed record DlsiteWorkMetadata(
    string Title,
    string Circle,
    string ThumbnailUrl,
    IReadOnlyList<string> VoiceActors,
    IReadOnlyList<string> Genres,
    DateTime? ReleaseDate);
