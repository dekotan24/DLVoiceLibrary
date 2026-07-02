using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;

namespace DLVoiceLibrary.ViewModels;

/// <summary>アプリ全体で単一インスタンス。再生エンジンと再生キューを保持し、下部固定のミニプレーヤーバーを駆動する。</summary>
public partial class PlayerBarViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlayerService _player;
    private readonly IPlaybackQueueService _queue;
    private readonly IDatabaseService _database;
    private readonly ILogService _log;
    private readonly DispatcherTimer _persistTimer;

    public PlayerBarViewModel(IAudioPlayerService player, IPlaybackQueueService queue, IDatabaseService database, ILogService log)
    {
        _player = player;
        _queue = queue;
        _database = database;
        _log = log;

        _player.PositionChanged += OnPlayerPositionChanged;
        _player.PlaybackEnded += OnPlayerPlaybackEnded;
        _player.PlaybackError += OnPlayerError;

        _persistTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _persistTimer.Tick += async (_, _) =>
        {
            // 一時停止中は再生位置が変わらないため、無駄なDB書き込みを避ける(終了時の保存は別途ShutdownAsyncで行う)
            if (!IsPlaying) return;
            await PersistCurrentPositionAsync();
        };
        _persistTimer.Start();
    }

    [ObservableProperty]
    private Track? _currentTrack;

    [ObservableProperty]
    private string _currentWorkTitle = string.Empty;

    [ObservableProperty]
    private string _thumbnailPath = string.Empty;

    [ObservableProperty]
    private TimeSpan _position;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _volume = 1.0;

    partial void OnVolumeChanged(double value) => _player.Volume = (float)Math.Clamp(value, 0.0, 1.0);

    [ObservableProperty]
    private double _playbackRate = 1.0;

    partial void OnPlaybackRateChanged(double value) => _player.Rate = (float)value;

    [ObservableProperty]
    private RepeatMode _repeatMode = RepeatMode.Off;

    partial void OnRepeatModeChanged(RepeatMode value) => _queue.RepeatMode = value;

    [ObservableProperty]
    private bool _shuffleOn;

    partial void OnShuffleOnChanged(bool value) => _queue.SetShuffle(value);

    public bool HasCurrentTrack => CurrentTrack is not null;

    /// <summary>音声出力デバイスID(空文字=システム既定)。変更は即座に再生エンジンへ適用され、設定として保存される。</summary>
    [ObservableProperty]
    private string _audioDeviceId = string.Empty;

    partial void OnAudioDeviceIdChanged(string value)
    {
        _player.SetOutputDevice(value);
        _ = SaveAppStateAsync();
    }

    /// <summary>設定画面用: 利用可能な音声出力デバイス一覧。</summary>
    public IReadOnlyList<AudioOutputDevice> EnumerateOutputDevices() => _player.EnumerateOutputDevices();

    [ObservableProperty]
    private bool _isCurrentTrackFavorite;

    partial void OnCurrentTrackChanged(Track? value) => IsCurrentTrackFavorite = value?.IsFavorite ?? false;

    [RelayCommand]
    private async Task ToggleCurrentTrackFavoriteAsync()
    {
        if (CurrentTrack is null) return;

        CurrentTrack.IsFavorite = !CurrentTrack.IsFavorite;
        IsCurrentTrackFavorite = CurrentTrack.IsFavorite;

        try
        {
            await _database.SetTrackFavoriteAsync(CurrentTrack.Id, CurrentTrack.IsFavorite);
        }
        catch (Exception ex)
        {
            _log.Error($"お気に入り状態の保存に失敗: track={CurrentTrack.Id}", ex);
        }
    }

    // ---------- スリープタイマー ----------

    [ObservableProperty]
    private TimeSpan? _sleepTimerRemaining;

    [ObservableProperty]
    private bool _sleepAtTrackEnd;

    private DispatcherTimer? _sleepCountdownTimer;

    /// <summary>指定した時間が経過したら自動的に一時停止する。</summary>
    public void StartSleepTimer(TimeSpan duration)
    {
        CancelSleepTimer();
        SleepTimerRemaining = duration;

        _sleepCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sleepCountdownTimer.Tick += (_, _) =>
        {
            if (SleepTimerRemaining is not { } remaining) return;

            remaining -= TimeSpan.FromSeconds(1);
            if (remaining <= TimeSpan.Zero)
            {
                CancelSleepTimer();
                _player.Pause();
                IsPlaying = false;
            }
            else
            {
                SleepTimerRemaining = remaining;
            }
        };
        _sleepCountdownTimer.Start();
    }

    /// <summary>現在再生中のトラックが終わったタイミングで自動的に停止する(次のトラックへ進まない)。</summary>
    public void StartSleepAtTrackEnd()
    {
        CancelSleepTimer();
        SleepAtTrackEnd = true;
    }

    [RelayCommand]
    private void CancelSleepTimer()
    {
        _sleepCountdownTimer?.Stop();
        _sleepCountdownTimer = null;
        SleepTimerRemaining = null;
        SleepAtTrackEnd = false;
    }

    /// <summary>作品全曲再生・プレイリスト再生の共通入口。startTrackを指定すると途中の曲から再生を開始する。</summary>
    public async Task PlayQueueAsync(IReadOnlyList<Track> tracks, Track startTrack, string workTitle, string thumbnailPath)
    {
        _queue.RepeatMode = RepeatMode;
        _queue.LoadQueue(tracks, startTrack);
        CurrentWorkTitle = workTitle;
        ThumbnailPath = thumbnailPath;

        var current = _queue.CurrentTrack ?? startTrack;
        await PlayTrackInternalAsync(current, autoPlay: true);
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (CurrentTrack is null) return;

        if (IsPlaying)
        {
            _player.Pause();
            IsPlaying = false;
        }
        else
        {
            _player.Play();
            IsPlaying = true;
        }
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        var next = _queue.MoveNext();
        if (next is null)
        {
            _player.Stop();
            IsPlaying = false;
            return;
        }
        await PlayTrackInternalAsync(next, autoPlay: true);
    }

    [RelayCommand]
    private async Task PreviousAsync()
    {
        // 3秒以上経過していたら曲頭に戻る、それ未満なら本当に前の曲へ(一般的な音楽プレーヤーの挙動)
        if (_player.Position.TotalSeconds >= 3)
        {
            _player.Seek(TimeSpan.Zero);
            Position = TimeSpan.Zero;
            return;
        }

        var previous = _queue.MovePrevious();
        if (previous is null)
        {
            _player.Seek(TimeSpan.Zero);
            Position = TimeSpan.Zero;
            return;
        }
        await PlayTrackInternalAsync(previous, autoPlay: true);
    }

    [RelayCommand]
    private void Seek(double seconds) => _player.Seek(TimeSpan.FromSeconds(seconds));

    [RelayCommand]
    private void ToggleRepeat()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.One,
            RepeatMode.One => RepeatMode.All,
            _ => RepeatMode.Off
        };
    }

    [RelayCommand]
    private void ToggleShuffle() => ShuffleOn = !ShuffleOn;

    private async Task PlayTrackInternalAsync(Track track, bool autoPlay)
    {
        if (CurrentTrack is not null && CurrentTrack.Id != track.Id)
        {
            await PersistCurrentPositionAsync();
        }

        CurrentTrack = track;
        Duration = track.Duration;
        Position = TimeSpan.Zero;
        _player.Load(track.FilePath);

        var resumeFrom = TimeSpan.FromMilliseconds(track.ResumePositionMs);

        if (autoPlay)
        {
            _player.Play();
            IsPlaying = true;
        }

        if (resumeFrom > TimeSpan.Zero && (track.Duration == TimeSpan.Zero || resumeFrom < track.Duration))
        {
            _player.Seek(resumeFrom);
            Position = resumeFrom;
        }

        track.PlayCount++;
        track.LastPlayedAt = DateTime.Now;

        try
        {
            await _database.UpdateTrackPlaybackStateAsync(track.Id, track.ResumePositionMs, track.PlayCount, track.LastPlayedAt.Value);
            await _database.AddPlayHistoryAsync(track.Id, DateTime.Now);
            await SaveAppStateAsync();
        }
        catch (Exception ex)
        {
            _log.Error($"再生状態の保存に失敗: track={track.Id}", ex);
        }
    }

    private async Task PersistCurrentPositionAsync()
    {
        if (CurrentTrack is null) return;

        try
        {
            CurrentTrack.ResumePositionMs = (long)_player.Position.TotalMilliseconds;
            await _database.UpdateTrackPlaybackStateAsync(
                CurrentTrack.Id, CurrentTrack.ResumePositionMs, CurrentTrack.PlayCount,
                CurrentTrack.LastPlayedAt ?? DateTime.Now);
            await SaveAppStateAsync();
        }
        catch (Exception ex)
        {
            _log.Error("再生位置の保存に失敗", ex);
        }
    }

    private async Task SaveAppStateAsync()
    {
        await _database.SaveAppStateAsync(new AppState
        {
            LastTrackId = CurrentTrack?.Id,
            LastPositionMs = (long)Position.TotalMilliseconds,
            Volume = Volume,
            RepeatMode = RepeatMode,
            ShuffleOn = ShuffleOn,
            PlaybackRate = PlaybackRate,
            AudioDeviceId = AudioDeviceId
        });
    }

    /// <summary>アプリ起動時、前回の続きから再生できる状態を復元する(自動再生はしない)。</summary>
    public async Task RestoreLastSessionAsync()
    {
        try
        {
            var state = await _database.GetAppStateAsync();
            Volume = state.Volume;
            RepeatMode = state.RepeatMode;
            PlaybackRate = state.PlaybackRate;
            ShuffleOn = state.ShuffleOn;
            AudioDeviceId = state.AudioDeviceId;

            if (state.LastTrackId is not { } trackId) return;

            var track = await _database.GetTrackByIdAsync(trackId);
            if (track is null) return;

            CurrentTrack = track;
            Duration = track.Duration;
            Position = TimeSpan.FromMilliseconds(state.LastPositionMs);
            _player.Load(track.FilePath);
            _player.Seek(Position);

            var work = await _database.GetWorkByIdAsync(track.WorkId);
            if (work is not null)
            {
                CurrentWorkTitle = work.Title;
                ThumbnailPath = work.ThumbnailPath;
            }
        }
        catch (Exception ex)
        {
            _log.Error("前回セッションの復元に失敗", ex);
        }
    }

    /// <summary>アプリ終了時に呼び出し、現在の再生状態を確実に保存する。</summary>
    public async Task ShutdownAsync()
    {
        _persistTimer.Stop();
        await PersistCurrentPositionAsync();
    }

    private void OnPlayerPositionChanged(object? sender, TimeSpan position)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => Position = position);
    }

    private void OnPlayerPlaybackEnded(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await HandlePlaybackEndedAsync();
            }
            catch (Exception ex)
            {
                _log.Error("再生終了後の次トラック遷移に失敗", ex);
            }
        });
    }

    private async Task HandlePlaybackEndedAsync()
    {
        if (SleepAtTrackEnd)
        {
            CancelSleepTimer();
            IsPlaying = false;
            return;
        }

        if (RepeatMode == RepeatMode.One)
        {
            _player.Seek(TimeSpan.Zero);
            _player.Play();
            return;
        }

        var next = _queue.MoveNext();
        if (next is null)
        {
            IsPlaying = false;
            return;
        }

        await PlayTrackInternalAsync(next, autoPlay: true);
    }

    private void OnPlayerError(object? sender, string message)
    {
        _log.Error($"再生エラー: {message}");
        Application.Current?.Dispatcher.BeginInvoke(() => IsPlaying = false);
    }

    public void Dispose()
    {
        _persistTimer.Stop();
        _sleepCountdownTimer?.Stop();
        _player.PositionChanged -= OnPlayerPositionChanged;
        _player.PlaybackEnded -= OnPlayerPlaybackEnded;
        _player.PlaybackError -= OnPlayerError;
    }
}
