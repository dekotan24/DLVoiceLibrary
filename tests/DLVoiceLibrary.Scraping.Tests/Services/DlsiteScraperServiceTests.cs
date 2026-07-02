using DLVoiceLibrary.Scraping;

namespace DLVoiceLibrary.Scraping.Tests.Services;

public class DlsiteScraperServiceTests
{
    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine("Fixtures", fileName));

    /// <summary>
    /// DLsite実ページのHTMLフィクスチャ(RJ*.html)は著作物を含むため公開リポジトリに含めていない。
    /// フィクスチャが手元にある環境でのみ実HTMLベースの検証を行い、無ければ黙って成功扱いにする
    /// (合成HTMLベースのテストは常に実行される)。
    /// </summary>
    private static string? TryReadRealPageFixture(string fileName)
    {
        var path = Path.Combine("Fixtures", fileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    [Fact]
    public void ParseHtml_WithTwoVoiceActors_ExtractsAllFields()
    {
        var html = TryReadRealPageFixture("RJ01009404_with_voice_actors.html");
        if (html is null) return; // フィクスチャ未配置の環境ではスキップ

        var result = DlsiteScraperService.ParseHtml(html);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.Title));
        Assert.Equal("ぱちぱちぼいす", result.Circle);

        Assert.Equal(2, result.VoiceActors.Count);
        Assert.Equal("天知遥", result.VoiceActors[0]);
        Assert.Equal("恋羽もこ", result.VoiceActors[1]);

        Assert.Equal(8, result.Genres.Count);
        Assert.Equal("ASMR", result.Genres[0]);

        Assert.Equal(new DateTime(2023, 2, 18), result.ReleaseDate);
    }

    [Fact]
    public void ParseHtml_WithSingleVoiceActor_ExtractsOneVoiceActor()
    {
        var html = TryReadRealPageFixture("RJ01093725_single_voice_actor.html");
        if (html is null) return; // フィクスチャ未配置の環境ではスキップ

        var result = DlsiteScraperService.ParseHtml(html);

        Assert.NotNull(result);
        Assert.Single(result!.VoiceActors);
    }

    [Fact]
    public void ParseHtml_WithoutVoiceActorSection_ReturnsEmptyList()
    {
        var html = ReadFixture("minimal_no_voice_actor.html");

        var result = DlsiteScraperService.ParseHtml(html);

        Assert.NotNull(result);
        Assert.Equal("声優欄なしテスト作品", result!.Title);
        Assert.Equal("テストサークル", result.Circle);
        Assert.NotNull(result.VoiceActors);
        Assert.Empty(result.VoiceActors);
        Assert.Equal(2, result.Genres.Count);
        Assert.Equal(new DateTime(2024, 1, 5), result.ReleaseDate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html></html>")]
    [InlineData("<html><body><p>no work_name here</p></body></html>")]
    public void ParseHtml_WithMissingWorkName_ReturnsNull(string html)
    {
        var result = DlsiteScraperService.ParseHtml(html);

        Assert.Null(result);
    }

    [Fact]
    public void ParseHtml_WithProtocolRelativeThumbnail_PrependsHttps()
    {
        const string html = """
            <!DOCTYPE html>
            <html>
            <head>
            <meta property="og:image" content="//img.dlsite.jp/modpub/images2/work/doujin/RJ00000000/RJ00000001_img_main.jpg">
            </head>
            <body>
            <h1 itemprop="name" id="work_name">プロトコル相対URLテスト作品</h1>
            <span itemprop="brand" class="maker_name"><a href="#">テストサークル</a></span>
            </body>
            </html>
            """;

        var result = DlsiteScraperService.ParseHtml(html);

        Assert.NotNull(result);
        Assert.Equal("https://img.dlsite.jp/modpub/images2/work/doujin/RJ00000000/RJ00000001_img_main.jpg", result!.ThumbnailUrl);
    }
}
