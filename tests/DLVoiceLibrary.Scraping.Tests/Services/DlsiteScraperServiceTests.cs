using DLVoiceLibrary.Scraping;

namespace DLVoiceLibrary.Scraping.Tests.Services;

/// <summary>
/// メタデータ取得はDLsiteInfoGetterライブラリに統合したため、HTMLパースのテストは廃止した。
/// (実際の取得ロジックはライブラリ側の責務。ここではID種別のディスパッチ判定のみ検証する)
/// </summary>
public class DlsiteScraperServiceTests
{
    [Theory]
    [InlineData("d_750863")]
    [InlineData("D_750863")] // 大文字始まりでも判定できる
    public void IsFanzaId_FanzaCid_ReturnsTrue(string productId)
    {
        Assert.True(DlsiteScraperService.IsFanzaId(productId));
    }

    [Theory]
    [InlineData("RJ01005349")]
    [InlineData("VJ014441")]
    [InlineData("BJ123456")]
    [InlineData("")]
    public void IsFanzaId_DlsiteIdOrEmpty_ReturnsFalse(string productId)
    {
        Assert.False(DlsiteScraperService.IsFanzaId(productId));
    }
}
