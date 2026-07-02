using System.Text.RegularExpressions;

namespace DLVoiceLibrary.Core.Services;

public sealed partial class FolderScanService : IFolderScanService
{
    private static readonly string[] SupportedExtensions = [".mp3", ".wav", ".flac", ".ogg", ".m4a"];

    private readonly ITagReaderService _tagReader;

    public FolderScanService(ITagReaderService tagReader)
    {
        _tagReader = tagReader;
    }

    public ProductIdMatch? ExtractProductId(string folderName)
    {
        var match = ProductIdRegex().Match(folderName);
        if (!match.Success)
        {
            return null;
        }

        return new ProductIdMatch(match.Value.ToUpperInvariant(), "DLsite");
    }

    public List<ScannedTrack> ScanTracks(string workFolderPath)
    {
        var result = new List<ScannedTrack>();
        var trackNo = 0;
        ScanDirectory(workFolderPath, result, ref trackNo);
        return result;
    }

    private void ScanDirectory(string directoryPath, List<ScannedTrack> result, ref int trackNo)
    {
        var files = Directory.EnumerateFiles(directoryPath)
            .OrderBy(f => Path.GetFileName(f), NaturalSortComparer.Instance)
            .ToList();

        foreach (var filePath in files)
        {
            var extension = Path.GetExtension(filePath);
            if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var tags = _tagReader.ReadTags(filePath);
            trackNo++;
            result.Add(new ScannedTrack(
                filePath,
                trackNo,
                tags.Title,
                tags.Artist,
                tags.DurationMs,
                extension.TrimStart('.').ToLowerInvariant()));
        }

        var subDirectories = Directory.EnumerateDirectories(directoryPath)
            .OrderBy(d => Path.GetFileName(d), NaturalSortComparer.Instance)
            .ToList();

        foreach (var subDirectory in subDirectories)
        {
            ScanDirectory(subDirectory, result, ref trackNo);
        }
    }

    [GeneratedRegex(@"(RJ|VJ|BJ|RG|RE)(\d{6,8})", RegexOptions.IgnoreCase)]
    private static partial Regex ProductIdRegex();

    private sealed class NaturalSortComparer : IComparer<string>
    {
        public static readonly NaturalSortComparer Instance = new();

        // char.IsDigitは全角数字「１２３」等にもtrueを返すが、BigInteger.Parseは全角を解釈できず
        // FormatExceptionで落ちる(実ライブラリの一括登録で発生した実バグ)。
        // 数値扱いはASCIIの0-9に限定し、全角数字は通常の文字列として比較する。
        private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

        public int Compare(string? x, string? y)
        {
            if (x is null) return y is null ? 0 : -1;
            if (y is null) return 1;

            var partsX = SplitIntoParts(x);
            var partsY = SplitIntoParts(y);

            var count = Math.Min(partsX.Count, partsY.Count);
            for (var i = 0; i < count; i++)
            {
                var partX = partsX[i];
                var partY = partsY[i];

                var bothNumeric = partX.Length > 0 && partY.Length > 0
                    && IsAsciiDigit(partX[0]) && IsAsciiDigit(partY[0]);

                int comparison;
                if (bothNumeric)
                {
                    var numX = System.Numerics.BigInteger.Parse(partX);
                    var numY = System.Numerics.BigInteger.Parse(partY);
                    comparison = numX.CompareTo(numY);
                    if (comparison == 0)
                    {
                        // 数値としては同じでも、桁数(ゼロ埋め有無)で安定した順序にするために文字列長で比較
                        comparison = partX.Length.CompareTo(partY.Length);
                    }
                }
                else
                {
                    comparison = string.Compare(partX, partY, StringComparison.OrdinalIgnoreCase);
                }

                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return partsX.Count.CompareTo(partsY.Count);
        }

        private static List<string> SplitIntoParts(string input)
        {
            var parts = new List<string>();
            var i = 0;
            while (i < input.Length)
            {
                var start = i;
                var isDigit = IsAsciiDigit(input[i]);
                while (i < input.Length && IsAsciiDigit(input[i]) == isDigit)
                {
                    i++;
                }
                parts.Add(input[start..i]);
            }
            return parts;
        }
    }
}
