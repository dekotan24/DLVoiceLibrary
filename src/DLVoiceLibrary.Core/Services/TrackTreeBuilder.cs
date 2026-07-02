using System.Collections.ObjectModel;
using DLVoiceLibrary.Core.Models;

namespace DLVoiceLibrary.Core.Services;

/// <summary>
/// 作品のトラック一覧(DBからtrack_no順で取得済み)を、実際のフォルダ階層をそのまま反映したツリーに変換する。
/// トラックを名寄せ・統合したりフラット化したりはしない(ユーザー要件: フォルダ階層をそのまま維持する)。
/// </summary>
public static class TrackTreeBuilder
{
    public static ObservableCollection<TrackTreeNode> Build(string workFolderPath, IReadOnlyList<Track> tracksInOrder)
    {
        var root = new ObservableCollection<TrackTreeNode>();
        var folderLookup = new Dictionary<string, TrackTreeNode>();

        foreach (var track in tracksInOrder)
        {
            var relativePath = GetRelativePathSafe(workFolderPath, track.FilePath);
            var segments = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

            var currentChildren = root;
            var pathKey = string.Empty;

            // 最後のセグメント(ファイル名)以外はフォルダノード
            for (var i = 0; i < segments.Length - 1; i++)
            {
                pathKey = pathKey.Length == 0 ? segments[i] : $"{pathKey}\\{segments[i]}";

                if (!folderLookup.TryGetValue(pathKey, out var folderNode))
                {
                    folderNode = new TrackTreeNode(segments[i], isTrack: false);
                    folderLookup[pathKey] = folderNode;
                    currentChildren.Add(folderNode);
                }

                currentChildren = folderNode.Children;
            }

            var fileName = segments.Length > 0 ? segments[^1] : Path.GetFileName(track.FilePath);
            currentChildren.Add(new TrackTreeNode(fileName, isTrack: true, track: track));
        }

        return root;
    }

    private static string GetRelativePathSafe(string basePath, string fullPath)
    {
        try
        {
            return Path.GetRelativePath(basePath, fullPath);
        }
        catch
        {
            return Path.GetFileName(fullPath);
        }
    }
}
