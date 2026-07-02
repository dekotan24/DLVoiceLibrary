using System.Globalization;
using AngleSharp.Html.Parser;

namespace DLVoiceLibrary.Scraping;

public sealed class DlsiteScraperService : IDlsiteScraperService
{
    private readonly HttpClient _httpClient;

    public DlsiteScraperService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<DlsiteWorkMetadata?> FetchAsync(string productId, CancellationToken ct = default)
    {
        try
        {
            var path = productId.StartsWith("VJ", StringComparison.OrdinalIgnoreCase) ? "pro" : "maniax";
            var url = $"https://www.dlsite.com/{path}/work/=/product_id/{productId}.html";
            var html = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
            return ParseHtml(html);
        }
        catch
        {
            return null;
        }
    }

    internal static DlsiteWorkMetadata? ParseHtml(string html)
    {
        try
        {
            var parser = new HtmlParser();
            var doc = parser.ParseDocument(html);

            var title = doc.QuerySelector("#work_name")?.TextContent.Trim();
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            var circle = doc.QuerySelector("span.maker_name > a")?.TextContent.Trim() ?? string.Empty;

            var thumbnailUrl = doc.QuerySelector("meta[property='og:image']")?.GetAttribute("content") ?? string.Empty;
            if (thumbnailUrl.StartsWith("//", StringComparison.Ordinal))
            {
                thumbnailUrl = "https:" + thumbnailUrl;
            }

            var voiceActors = doc.QuerySelectorAll("#work_outline tr > th:contains('声優') + td > a")
                .Select(a => a.TextContent.Trim())
                .ToList();

            var genres = doc.QuerySelectorAll("#work_outline tr > th:contains('ジャンル') + td a")
                .Select(a => a.TextContent.Trim())
                .ToList();

            DateTime? releaseDate = null;
            var releaseDateText = doc.QuerySelector("#work_outline tr > th:contains('販売日') + td > a")?.TextContent.Trim();
            if (!string.IsNullOrEmpty(releaseDateText)
                && DateTime.TryParseExact(releaseDateText, "yyyy'年'MM'月'dd'日'", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                releaseDate = parsed;
            }

            return new DlsiteWorkMetadata(title, circle, thumbnailUrl, voiceActors, genres, releaseDate);
        }
        catch
        {
            return null;
        }
    }
}
