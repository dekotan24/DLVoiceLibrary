using DLVoiceLibrary.Core.Models;

namespace DLVoiceLibrary.Core.Services;

/// <summary>
/// <see cref="IPlaybackQueueService"/> の実装。再生順(シャッフル含む)とインデックス操作のみを扱う純粋ロジック。
/// </summary>
public sealed class PlaybackQueueService : IPlaybackQueueService
{
    private readonly Random _random = new();

    private List<Track> _originalOrder = new();
    private List<Track> _tracks = new();

    public IReadOnlyList<Track> Tracks => _tracks;

    public int CurrentIndex { get; private set; } = -1;

    public Track? CurrentTrack =>
        CurrentIndex >= 0 && CurrentIndex < _tracks.Count ? _tracks[CurrentIndex] : null;

    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;

    public bool ShuffleOn { get; private set; }

    public bool HasNext
    {
        get
        {
            if (_tracks.Count == 0)
            {
                return false;
            }

            return RepeatMode switch
            {
                RepeatMode.One or RepeatMode.All => true,
                _ => CurrentIndex < _tracks.Count - 1
            };
        }
    }

    public bool HasPrevious
    {
        get
        {
            if (_tracks.Count == 0)
            {
                return false;
            }

            return RepeatMode == RepeatMode.All || CurrentIndex > 0;
        }
    }

    public void LoadQueue(IReadOnlyList<Track> tracks, Track? startTrack = null)
    {
        _originalOrder = new List<Track>(tracks);
        _tracks = new List<Track>(_originalOrder);

        var startIndex = 0;
        if (startTrack is not null)
        {
            var foundIndex = _tracks.FindIndex(t => t.Id == startTrack.Id);
            if (foundIndex >= 0)
            {
                startIndex = foundIndex;
            }
        }

        if (_tracks.Count == 0)
        {
            CurrentIndex = -1;
            return;
        }

        CurrentIndex = startIndex;

        if (ShuffleOn)
        {
            ShuffleFromCurrent();
        }
    }

    public void SetShuffle(bool on)
    {
        if (on == ShuffleOn)
        {
            return;
        }

        ShuffleOn = on;

        if (_tracks.Count == 0)
        {
            return;
        }

        if (on)
        {
            ShuffleFromCurrent();
        }
        else
        {
            var currentId = CurrentTrack?.Id;
            _tracks = new List<Track>(_originalOrder);
            CurrentIndex = currentId is null
                ? 0
                : Math.Max(0, _tracks.FindIndex(t => t.Id == currentId.Value));
        }
    }

    public Track? MoveNext()
    {
        if (_tracks.Count == 0)
        {
            return null;
        }

        if (RepeatMode == RepeatMode.One)
        {
            return CurrentTrack;
        }

        var nextIndex = CurrentIndex + 1;
        if (nextIndex >= _tracks.Count)
        {
            if (RepeatMode == RepeatMode.All)
            {
                CurrentIndex = 0;
                return CurrentTrack;
            }

            CurrentIndex = _tracks.Count - 1;
            return null;
        }

        CurrentIndex = nextIndex;
        return CurrentTrack;
    }

    public Track? MovePrevious()
    {
        if (_tracks.Count == 0)
        {
            return null;
        }

        var prevIndex = CurrentIndex - 1;
        if (prevIndex < 0)
        {
            if (RepeatMode == RepeatMode.All)
            {
                CurrentIndex = _tracks.Count - 1;
                return CurrentTrack;
            }

            CurrentIndex = 0;
            return null;
        }

        CurrentIndex = prevIndex;
        return CurrentTrack;
    }

    /// <summary>現在トラックを先頭に固定し、残りをFisher-Yatesでシャッフルする。</summary>
    private void ShuffleFromCurrent()
    {
        var current = CurrentTrack;
        var rest = new List<Track>(_tracks);
        if (current is not null)
        {
            var currentPos = rest.FindIndex(t => t.Id == current.Id);
            if (currentPos >= 0)
            {
                rest.RemoveAt(currentPos);
            }
        }

        for (var i = rest.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (rest[i], rest[j]) = (rest[j], rest[i]);
        }

        var shuffled = new List<Track>(_tracks.Count);
        if (current is not null)
        {
            shuffled.Add(current);
        }
        shuffled.AddRange(rest);

        _tracks = shuffled;
        CurrentIndex = 0;
    }
}
