using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;
using Xunit;

namespace DLVoiceLibrary.Core.Tests.Services;

public sealed class TrackTreeBuilderTests
{
    private static Track MakeTrack(string filePath, int trackNo) => new()
    {
        FilePath = filePath,
        TrackNo = trackNo,
        Title = Path.GetFileNameWithoutExtension(filePath)
    };

    [Fact]
    public void Build_FlatFiles_ProducesLeafNodesOnly()
    {
        const string workFolder = @"C:\voice\work1";
        var tracks = new List<Track>
        {
            MakeTrack(@"C:\voice\work1\01.mp3", 1),
            MakeTrack(@"C:\voice\work1\02.mp3", 2)
        };

        var tree = TrackTreeBuilder.Build(workFolder, tracks);

        Assert.Equal(2, tree.Count);
        Assert.All(tree, node => Assert.True(node.IsTrack));
        Assert.Equal("01.mp3", tree[0].Name);
        Assert.Equal("02.mp3", tree[1].Name);
    }

    [Fact]
    public void Build_NestedVariantFolders_PreservesHierarchyWithoutMerging()
    {
        const string workFolder = @"C:\voice\work2";
        var tracks = new List<Track>
        {
            MakeTrack(@"C:\voice\work2\SEあり\01.wav", 1),
            MakeTrack(@"C:\voice\work2\SEあり\02.wav", 2),
            MakeTrack(@"C:\voice\work2\SEなし\01.wav", 3),
            MakeTrack(@"C:\voice\work2\SEなし\02.wav", 4)
        };

        var tree = TrackTreeBuilder.Build(workFolder, tracks);

        Assert.Equal(2, tree.Count);
        Assert.False(tree[0].IsTrack);
        Assert.Equal("SEあり", tree[0].Name);
        Assert.Equal(2, tree[0].Children.Count);
        Assert.False(tree[1].IsTrack);
        Assert.Equal("SEなし", tree[1].Name);
        Assert.Equal(2, tree[1].Children.Count);
    }

    [Fact]
    public void Build_DeeplyNestedFolder_BuildsMultiLevelTree()
    {
        const string workFolder = @"C:\voice\work3";
        var tracks = new List<Track>
        {
            MakeTrack(@"C:\voice\work3\本編\SEあり\mp3\track1.mp3", 1)
        };

        var tree = TrackTreeBuilder.Build(workFolder, tracks);

        var level1 = Assert.Single(tree);
        Assert.Equal("本編", level1.Name);
        var level2 = Assert.Single(level1.Children);
        Assert.Equal("SEあり", level2.Name);
        var level3 = Assert.Single(level2.Children);
        Assert.Equal("mp3", level3.Name);
        var leaf = Assert.Single(level3.Children);
        Assert.True(leaf.IsTrack);
        Assert.Equal("track1.mp3", leaf.Name);
    }

    [Fact]
    public void Build_PreservesTrackNoOrderWithinSameFolder()
    {
        const string workFolder = @"C:\voice\work4";
        var tracks = new List<Track>
        {
            MakeTrack(@"C:\voice\work4\track1.mp3", 1),
            MakeTrack(@"C:\voice\work4\track10.mp3", 2),
            MakeTrack(@"C:\voice\work4\track2.mp3", 3)
        };

        var tree = TrackTreeBuilder.Build(workFolder, tracks);

        Assert.Equal(["track1.mp3", "track10.mp3", "track2.mp3"], tree.Select(n => n.Name).ToArray());
    }

    [Fact]
    public void Build_EmptyTrackList_ReturnsEmptyTree()
    {
        var tree = TrackTreeBuilder.Build(@"C:\voice\empty", new List<Track>());
        Assert.Empty(tree);
    }
}
