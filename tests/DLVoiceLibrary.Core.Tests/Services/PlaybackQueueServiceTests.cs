using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;
using Xunit;

namespace DLVoiceLibrary.Core.Tests.Services;

public sealed class PlaybackQueueServiceTests
{
    private static Track MakeTrack(long id, string title) => new()
    {
        Id = id,
        Title = title
    };

    private static List<Track> MakeTracks(int count)
    {
        var tracks = new List<Track>();
        for (var i = 1; i <= count; i++)
        {
            tracks.Add(MakeTrack(i, $"Track{i}"));
        }
        return tracks;
    }

    [Fact]
    public void LoadQueue_WithoutStartTrack_CurrentTrackIsFirst()
    {
        var service = new PlaybackQueueService();
        var tracks = MakeTracks(3);

        service.LoadQueue(tracks);

        Assert.Equal(0, service.CurrentIndex);
        Assert.Equal(1, service.CurrentTrack?.Id);
    }

    [Fact]
    public void LoadQueue_WithStartTrack_CurrentTrackIsStartTrack()
    {
        var service = new PlaybackQueueService();
        var tracks = MakeTracks(5);

        service.LoadQueue(tracks, tracks[2]);

        Assert.Equal(2, service.CurrentIndex);
        Assert.Equal(3, service.CurrentTrack?.Id);
    }

    [Fact]
    public void LoadQueue_WithStartTrackNotFound_StartsAtIndexZero()
    {
        var service = new PlaybackQueueService();
        var tracks = MakeTracks(3);
        var unknown = MakeTrack(999, "Unknown");

        service.LoadQueue(tracks, unknown);

        Assert.Equal(0, service.CurrentIndex);
        Assert.Equal(1, service.CurrentTrack?.Id);
    }

    [Fact]
    public void LoadQueue_EmptyList_CurrentTrackIsNullAndHasNextPreviousFalse()
    {
        var service = new PlaybackQueueService();

        service.LoadQueue(new List<Track>());

        Assert.Null(service.CurrentTrack);
        Assert.False(service.HasNext);
        Assert.False(service.HasPrevious);
        Assert.Null(service.MoveNext());
        Assert.Null(service.MovePrevious());
    }

    [Fact]
    public void MoveNext_RepeatOff_StopsAtEndAndHasNextFalse()
    {
        var service = new PlaybackQueueService { RepeatMode = RepeatMode.Off };
        service.LoadQueue(MakeTracks(3));

        Assert.Equal(2, service.MoveNext()?.Id);
        Assert.True(service.HasNext);

        // move to last track (id 3)
        Assert.Equal(3, service.MoveNext()?.Id);
        Assert.False(service.HasNext);

        // beyond the end -> null, does not advance further
        Assert.Null(service.MoveNext());
        Assert.False(service.HasNext);
    }

    [Fact]
    public void MoveNext_RepeatAll_WrapsToStart()
    {
        var service = new PlaybackQueueService { RepeatMode = RepeatMode.All };
        service.LoadQueue(MakeTracks(3));

        service.MoveNext(); // id 2
        service.MoveNext(); // id 3 (last)

        var wrapped = service.MoveNext();

        Assert.Equal(1, wrapped?.Id);
        Assert.Equal(0, service.CurrentIndex);
    }

    [Fact]
    public void MovePrevious_RepeatAll_WrapsToEnd()
    {
        var service = new PlaybackQueueService { RepeatMode = RepeatMode.All };
        service.LoadQueue(MakeTracks(3));

        var wrapped = service.MovePrevious();

        Assert.Equal(3, wrapped?.Id);
        Assert.Equal(2, service.CurrentIndex);
    }

    [Fact]
    public void MovePrevious_RepeatOff_StaysAtStartAndReturnsNull()
    {
        var service = new PlaybackQueueService { RepeatMode = RepeatMode.Off };
        service.LoadQueue(MakeTracks(3));

        var result = service.MovePrevious();

        Assert.Null(result);
        Assert.Equal(0, service.CurrentIndex);
        Assert.False(service.HasPrevious);
    }

    [Fact]
    public void MoveNext_RepeatOne_ReturnsSameTrackAndDoesNotAdvance()
    {
        var service = new PlaybackQueueService { RepeatMode = RepeatMode.One };
        service.LoadQueue(MakeTracks(3));

        var result = service.MoveNext();

        Assert.Equal(1, result?.Id);
        Assert.Equal(0, service.CurrentIndex);
    }

    [Fact]
    public void SetShuffleTrue_CurrentTrackMovesToIndexZero_CurrentTrackUnchanged()
    {
        var service = new PlaybackQueueService();
        var tracks = MakeTracks(5);
        service.LoadQueue(tracks, tracks[2]); // current = id 3

        var currentBefore = service.CurrentTrack;
        service.SetShuffle(true);

        Assert.True(service.ShuffleOn);
        Assert.Equal(0, service.CurrentIndex);
        Assert.Equal(currentBefore?.Id, service.CurrentTrack?.Id);
        Assert.Equal(3, service.CurrentTrack?.Id);
    }

    [Fact]
    public void SetShuffleFalseAfterTrue_RestoresOriginalOrderAndCurrentIndex()
    {
        var service = new PlaybackQueueService();
        var tracks = MakeTracks(5); // A=1,B=2,C=3,D=4,E=5
        service.LoadQueue(tracks, tracks[2]); // current = C (id 3, index 2)

        service.SetShuffle(true);
        service.SetShuffle(false);

        Assert.False(service.ShuffleOn);
        Assert.Equal(2, service.CurrentIndex);
        Assert.Equal(3, service.CurrentTrack?.Id);

        // order restored to original insertion order
        for (var i = 0; i < tracks.Count; i++)
        {
            Assert.Equal(tracks[i].Id, service.Tracks[i].Id);
        }
    }

    [Fact]
    public void SetShuffle_SameValue_IsNoOp()
    {
        var service = new PlaybackQueueService();
        var tracks = MakeTracks(5);
        service.LoadQueue(tracks, tracks[2]);

        service.SetShuffle(false); // already false -> no-op

        Assert.False(service.ShuffleOn);
        Assert.Equal(2, service.CurrentIndex);
        for (var i = 0; i < tracks.Count; i++)
        {
            Assert.Equal(tracks[i].Id, service.Tracks[i].Id);
        }

        service.SetShuffle(true);
        var orderAfterShuffle = service.Tracks.Select(t => t.Id).ToList();

        service.SetShuffle(true); // already true -> no-op

        Assert.True(service.ShuffleOn);
        Assert.Equal(orderAfterShuffle, service.Tracks.Select(t => t.Id).ToList());
    }

    [Fact]
    public void LoadQueue_WithShuffleAlreadyOn_ResultIsShuffledWithStartTrackFirst()
    {
        var service = new PlaybackQueueService();
        service.LoadQueue(MakeTracks(3));
        service.SetShuffle(true);

        var newTracks = MakeTracks(5);
        service.LoadQueue(newTracks, newTracks[3]); // start = id 4

        Assert.True(service.ShuffleOn);
        Assert.Equal(0, service.CurrentIndex);
        Assert.Equal(4, service.CurrentTrack?.Id);
        Assert.Equal(5, service.Tracks.Count);
    }
}
