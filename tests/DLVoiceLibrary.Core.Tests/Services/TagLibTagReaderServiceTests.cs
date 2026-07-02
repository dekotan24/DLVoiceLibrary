using System.Diagnostics;
using DLVoiceLibrary.Core.Services;
using Xunit;

namespace DLVoiceLibrary.Core.Tests.Services;

public sealed class TagLibTagReaderServiceTests : IDisposable
{
    private const string FfmpegPath = @"C:\Users\fanta\AppData\Local\JDownloader 2\tools\Windows\ffmpeg\x64\ffmpeg.exe";

    private readonly string _tempDir;
    private readonly TagLibTagReaderService _sut = new();

    private readonly string _taggedMp3Path;
    private readonly string _untaggedMp3Path;
    private readonly string _emptyFilePath;
    private readonly string _corruptMp3Path;

    public TagLibTagReaderServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dlvl_tagtest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _taggedMp3Path = Path.Combine(_tempDir, "tagged.mp3");
        _untaggedMp3Path = Path.Combine(_tempDir, "untagged.mp3");
        _emptyFilePath = Path.Combine(_tempDir, "empty.mp3");
        _corruptMp3Path = Path.Combine(_tempDir, "corrupt.mp3");

        RunFfmpeg(
            "-y -f lavfi -i \"sine=frequency=440:duration=1\" " +
            "-metadata title=\"テストタイトル\" -metadata artist=\"テストアーティスト\" -metadata track=3 " +
            $"-c:a libmp3lame \"{_taggedMp3Path}\"");

        RunFfmpeg(
            "-y -f lavfi -i \"sine=frequency=440:duration=1\" " +
            $"-c:a libmp3lame \"{_untaggedMp3Path}\"");

        File.WriteAllBytes(_emptyFilePath, Array.Empty<byte>());
        File.WriteAllText(_corruptMp3Path, "これは音声ファイルではありません。ただのテキストです。");
    }

    private static void RunFfmpeg(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FfmpegPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        process.WaitForExit();
    }

    [Fact]
    public void ReadTags_TaggedMp3_ReturnsTagValues()
    {
        var result = _sut.ReadTags(_taggedMp3Path);

        Assert.Equal("テストタイトル", result.Title);
        Assert.Equal("テストアーティスト", result.Artist);
        Assert.Equal(3, result.TrackNumber);
        Assert.InRange(result.DurationMs, 800, 1500);
    }

    [Fact]
    public void ReadTags_UntaggedMp3_FallsBackToFileNameAndEmptyArtist()
    {
        var result = _sut.ReadTags(_untaggedMp3Path);

        Assert.Equal("untagged", result.Title);
        Assert.Equal(string.Empty, result.Artist);
    }

    [Fact]
    public void ReadTags_NonExistentFile_DoesNotThrowAndFallsBackToFileName()
    {
        var missingPath = Path.Combine(_tempDir, "does_not_exist.mp3");

        var result = _sut.ReadTags(missingPath);

        Assert.Equal("does_not_exist", result.Title);
        Assert.Equal(string.Empty, result.Artist);
        Assert.Equal(0, result.TrackNumber);
        Assert.Equal(0, result.DurationMs);
    }

    [Fact]
    public void ReadTags_EmptyFile_DoesNotThrowAndFallsBackToFileName()
    {
        var result = _sut.ReadTags(_emptyFilePath);

        Assert.Equal("empty", result.Title);
        Assert.Equal(string.Empty, result.Artist);
        Assert.Equal(0, result.TrackNumber);
        Assert.Equal(0, result.DurationMs);
    }

    [Fact]
    public void ReadTags_CorruptFile_DoesNotThrowAndFallsBackToFileName()
    {
        var result = _sut.ReadTags(_corruptMp3Path);

        Assert.Equal("corrupt", result.Title);
        Assert.Equal(string.Empty, result.Artist);
        Assert.Equal(0, result.TrackNumber);
        Assert.Equal(0, result.DurationMs);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // テスト後片付けの失敗は無視する
        }
    }
}
