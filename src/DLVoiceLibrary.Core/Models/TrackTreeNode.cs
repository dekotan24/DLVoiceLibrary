using System.Collections.ObjectModel;

namespace DLVoiceLibrary.Core.Models;

/// <summary>作品フォルダの実際の階層構造をそのまま反映したツリーノード。フォルダ(IsTrack=false)は
/// 展開/折りたたみ可能なコンテナ、音声ファイル(IsTrack=true)は再生可能な葉ノード。</summary>
public sealed class TrackTreeNode
{
    public TrackTreeNode(string name, bool isTrack, Track? track = null)
    {
        Name = name;
        IsTrack = isTrack;
        Track = track;
    }

    public string Name { get; }
    public bool IsTrack { get; }
    public Track? Track { get; }
    public ObservableCollection<TrackTreeNode> Children { get; } = new();
}
