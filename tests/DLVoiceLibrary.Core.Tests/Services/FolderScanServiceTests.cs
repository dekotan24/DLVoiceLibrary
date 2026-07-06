using DLVoiceLibrary.Core.Services;
using Moq;
using Xunit;

namespace DLVoiceLibrary.Core.Tests.Services;

public sealed class FolderScanServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FolderScanService _sut;

    public FolderScanServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"dlvl_scan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        var tagReaderMock = new Mock<ITagReaderService>();
        tagReaderMock
            .Setup(t => t.ReadTags(It.IsAny<string>()))
            .Returns((string path) => new TrackTagInfo(Path.GetFileNameWithoutExtension(path), "", 0, 60000));

        _sut = new FolderScanService(tagReaderMock.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static void TouchFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
    }

    // ---------- ExtractProductId ----------

    [Theory]
    [InlineData("[RJ01005349] [サークル名] タイトル", "RJ01005349")]
    [InlineData("VJ01234567", "VJ01234567")]
    [InlineData("[bj123456] タイトル", "BJ123456")]
    [InlineData("RG654321 タイトル", "RG654321")]
    [InlineData("RE12345678作品名", "RE12345678")]
    public void ExtractProductId_ValidFolderName_ExtractsId(string folderName, string expectedId)
    {
        var result = _sut.ExtractProductId(folderName);

        Assert.NotNull(result);
        Assert.Equal(expectedId, result!.ProductId);
        Assert.Equal("DLsite", result.Source);
    }

    [Theory]
    [InlineData("[RJ12345] 5桁は対象外")]
    [InlineData("タイトルだけでIDなし")]
    [InlineData("XY01234567")]
    public void ExtractProductId_NoValidId_ReturnsNull(string folderName)
    {
        var result = _sut.ExtractProductId(folderName);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("[d_750863] [サークル名] タイトル", "d_750863")]
    [InlineData("d_123456", "d_123456")]
    [InlineData("D_750863 大文字表記でも小文字に正規化", "d_750863")]
    public void ExtractProductId_FanzaCid_ExtractsIdAsFanza(string folderName, string expectedId)
    {
        var result = _sut.ExtractProductId(folderName);

        Assert.NotNull(result);
        Assert.Equal(expectedId, result!.ProductId);
        Assert.Equal("FANZA", result.Source);
    }

    [Fact]
    public void ExtractProductId_BothDlsiteAndFanzaId_PrefersDlsite()
    {
        var result = _sut.ExtractProductId("[RJ01005349] d_750863 混在フォルダ名");

        Assert.NotNull(result);
        Assert.Equal("RJ01005349", result!.ProductId);
        Assert.Equal("DLsite", result.Source);
    }

    [Theory]
    [InlineData("sound_01 効果音集")]
    [InlineData("hd_1080p 動画フォルダ")]
    [InlineData("bgm_02")]
    public void ExtractProductId_AlphanumericPrefixBeforeDUnderscore_ReturnsNull(string folderName)
    {
        // 「英数字 + d_数字」の部分文字列をFANZAのcidと誤認しないこと
        var result = _sut.ExtractProductId(folderName);

        Assert.Null(result);
    }

    // ---------- 自然順ソート ----------

    [Fact]
    public void ScanTracks_ZeroPaddedFileNames_OrdersNumerically()
    {
        var names = Enumerable.Range(1, 12).Select(n => $"{n:D2}.wav").ToArray();
        foreach (var name in names)
        {
            TouchFile(Path.Combine(_tempRoot, name));
        }

        var tracks = _sut.ScanTracks(_tempRoot);

        Assert.Equal(12, tracks.Count);
        for (var i = 0; i < 12; i++)
        {
            Assert.Equal(i + 1, tracks[i].TrackNo);
            Assert.Equal($"{i + 1:D2}", tracks[i].Title);
        }
    }

    [Fact]
    public void ScanTracks_NonZeroPaddedFileNames_OrdersNaturallyNotLexically()
    {
        // track1, track2, ..., track10 が辞書順(track1, track10, track2...)ではなく自然順になること
        foreach (var n in new[] { 1, 2, 3, 10 })
        {
            TouchFile(Path.Combine(_tempRoot, $"track{n}.mp3"));
        }

        var tracks = _sut.ScanTracks(_tempRoot);

        Assert.Equal(["track1", "track2", "track3", "track10"], tracks.Select(t => t.Title).ToArray());
        Assert.Equal([1, 2, 3, 4], tracks.Select(t => t.TrackNo).ToArray());
    }

    [Fact]
    public void ScanTracks_FullWidthDigitsInFileNames_DoesNotThrow()
    {
        // 全角数字はchar.IsDigitがtrueを返すがBigInteger.Parseで解釈できず、
        // 実ライブラリの一括登録でFormatException→登録失敗になった回帰テスト。
        // 同人音声のファイル名には全角数字・丸数字が普通に混ざる。
        foreach (var name in new[] { "トラック１.mp3", "トラック２.mp3", "トラック１０.mp3", "本編③おまけ.mp3", "01 本編.mp3" })
        {
            TouchFile(Path.Combine(_tempRoot, name));
        }

        var tracks = _sut.ScanTracks(_tempRoot);

        Assert.Equal(5, tracks.Count);
        Assert.Equal([1, 2, 3, 4, 5], tracks.Select(t => t.TrackNo).ToArray());
    }

    [Fact]
    public void ScanTracks_MixedFullWidthAndAsciiDigits_DoesNotThrow()
    {
        // 全角数字とASCII数字が同一フォルダに混在してもソート比較器が例外を出さないこと
        foreach (var name in new[] { "１.wav", "2.wav", "３.wav", "10.wav", "ｖｏｌ２ 第3話.wav" })
        {
            TouchFile(Path.Combine(_tempRoot, name));
        }

        var tracks = _sut.ScanTracks(_tempRoot);

        Assert.Equal(5, tracks.Count);
    }

    // ---------- 深いネスト構造 ----------

    [Fact]
    public void ScanTracks_NestedParallelSubfolders_VisitsInNaturalOrderWithSequentialTrackNo()
    {
        TouchFile(Path.Combine(_tempRoot, "01_本編SEあり", "01.wav"));
        TouchFile(Path.Combine(_tempRoot, "01_本編SEあり", "02.wav"));
        TouchFile(Path.Combine(_tempRoot, "02_本編SEなし", "01.wav"));
        TouchFile(Path.Combine(_tempRoot, "02_本編SEなし", "02.wav"));

        var tracks = _sut.ScanTracks(_tempRoot);

        Assert.Equal(4, tracks.Count);
        Assert.Equal([1, 2, 3, 4], tracks.Select(t => t.TrackNo).ToArray());
        Assert.Contains("01_本編SEあり", tracks[0].FilePath);
        Assert.Contains("01_本編SEあり", tracks[1].FilePath);
        Assert.Contains("02_本編SEなし", tracks[2].FilePath);
        Assert.Contains("02_本編SEなし", tracks[3].FilePath);
    }

    [Fact]
    public void ScanTracks_DeeplyNestedFolders_FindsAllTracks()
    {
        TouchFile(Path.Combine(_tempRoot, "a", "b", "c", "d", "e", "01.mp3"));
        TouchFile(Path.Combine(_tempRoot, "a", "b", "c", "d", "e", "02.mp3"));

        var tracks = _sut.ScanTracks(_tempRoot);

        Assert.Equal(2, tracks.Count);
        Assert.Equal([1, 2], tracks.Select(t => t.TrackNo).ToArray());
    }

    // ---------- 音声以外のファイルの除外 ----------

    [Fact]
    public void ScanTracks_NonAudioFiles_AreIgnored()
    {
        TouchFile(Path.Combine(_tempRoot, "01.wav"));
        TouchFile(Path.Combine(_tempRoot, "illustration.jpg"));
        TouchFile(Path.Combine(_tempRoot, "script.pdf"));
        TouchFile(Path.Combine(_tempRoot, "readme.txt"));
        TouchFile(Path.Combine(_tempRoot, "promo.mp4"));

        var tracks = _sut.ScanTracks(_tempRoot);

        Assert.Single(tracks);
        Assert.EndsWith("01.wav", tracks[0].FilePath);
    }

    // ---------- 拡張子の大文字小文字 ----------

    [Theory]
    [InlineData("track.MP3", "mp3")]
    [InlineData("track.WAV", "wav")]
    [InlineData("track.Flac", "flac")]
    [InlineData("track.OGG", "ogg")]
    [InlineData("track.M4A", "m4a")]
    public void ScanTracks_UppercaseExtensions_AreRecognized(string fileName, string expectedFormat)
    {
        TouchFile(Path.Combine(_tempRoot, fileName));

        var tracks = _sut.ScanTracks(_tempRoot);

        Assert.Single(tracks);
        Assert.Equal(expectedFormat, tracks[0].FileFormat);
    }

    // ---------- 空フォルダ ----------

    [Fact]
    public void ScanTracks_EmptyFolder_ReturnsEmptyList()
    {
        var tracks = _sut.ScanTracks(_tempRoot);

        Assert.Empty(tracks);
    }

    [Fact]
    public void ScanTracks_FolderWithOnlySubfoldersNoAudio_ReturnsEmptyList()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "empty1"));
        TouchFile(Path.Combine(_tempRoot, "empty1", "note.txt"));

        var tracks = _sut.ScanTracks(_tempRoot);

        Assert.Empty(tracks);
    }
}
