using DLsiteInfoGetter;

namespace DLVoiceLibrary.Scraping;

/// <summary>
/// 作品メタデータの取得サービス。実際の取得はDLsiteInfoGetterライブラリに委譲する。
/// 以前は自前のHTMLスクレイピングだったが、AI生成作品(aix)等の新レイアウトページでは
/// サークル名等の要素が存在せず取得できなかったため、product.json APIベースのライブラリに統合した。
/// FANZA同人(d_123456形式)にも対応する。
/// </summary>
public sealed class DlsiteScraperService : IDlsiteScraperService
{
    public async Task<DlsiteWorkMetadata?> FetchAsync(string productId, CancellationToken ct = default)
    {
        try
        {
            if (IsFanzaId(productId))
            {
                var fanza = await Task.Run(() => FanzaInfo.GetInfo(productId), ct).ConfigureAwait(false);
                return ToMetadata(fanza.Title, fanza.Circle, fanza.ImageUrl, fanza.VoiceActor, fanza.Genre,
                    fanza.HasSellDate ? fanza.SellDate : null);
            }

            var dlsite = await Task.Run(() => DLsiteInfo.GetInfo(productId), ct).ConfigureAwait(false);
            return ToMetadata(dlsite.Title, dlsite.Circle, dlsite.ImageUrl, dlsite.VoiceActor, dlsite.Genre,
                dlsite.HasSellDate ? dlsite.SellDate : null);
        }
        catch
        {
            // インターフェース契約: ネットワークエラー・作品なし・パース失敗等、あらゆる失敗時はnullを返す
            return null;
        }
    }

    /// <summary>FANZA同人の作品ID(d_123456形式)かどうかを判定する。</summary>
    internal static bool IsFanzaId(string productId) =>
        productId.StartsWith("d_", StringComparison.OrdinalIgnoreCase);

    private static DlsiteWorkMetadata? ToMetadata(
        string title, string circle, string imageUrl,
        IReadOnlyList<string> voiceActors, IReadOnlyList<string> genres, DateTime? sellDate)
    {
        // タイトルが取れていない場合は取得失敗として扱う(旧実装のParseHtmlと同じ契約)
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }
        return new DlsiteWorkMetadata(title, circle, imageUrl, voiceActors, genres, sellDate);
    }
}
