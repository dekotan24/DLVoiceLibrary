using System;
using DLVoiceLibrary.Core.Services;
using LibVLCSharp.Shared;

namespace DLVoiceLibrary.Services;

/// <summary>
/// LibVLCSharpを利用した<see cref="IAudioPlayerService"/>の実装。
/// LibVLCネイティブイベントは非UIスレッドから発火するため、そのまま中継する
/// (UIスレッドへのマーシャリングは呼び出し側の責務)。
/// </summary>
public sealed class LibVlcAudioPlayerService : IAudioPlayerService
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _media;
    private bool _disposed;
    private string? _outputDeviceId;

    public LibVlcAudioPlayerService()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC("--no-video", "--quiet");
        _mediaPlayer = new MediaPlayer(_libVlc);

        _mediaPlayer.TimeChanged += OnTimeChanged;
        _mediaPlayer.EndReached += OnEndReached;
        _mediaPlayer.EncounteredError += OnEncounteredError;
    }

    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? PlaybackError;

    public void Load(string filePath)
    {
        _media?.Dispose();
        _media = new Media(_libVlc, new Uri(filePath));
        _mediaPlayer.Media = _media;

        // LibVLCはメディア切り替えで出力デバイス指定が既定に戻ることがあるため、ロードのたびに再適用する
        ApplyOutputDevice();
    }

    public IReadOnlyList<AudioOutputDevice> EnumerateOutputDevices()
    {
        var devices = new List<AudioOutputDevice> { new(string.Empty, "(システム既定)") };

        var enumerated = _mediaPlayer.AudioOutputDeviceEnum;
        if (enumerated is not null)
        {
            foreach (var device in enumerated)
            {
                devices.Add(new AudioOutputDevice(device.DeviceIdentifier, device.Description));
            }
        }
        return devices;
    }

    public void SetOutputDevice(string? deviceId)
    {
        _outputDeviceId = string.IsNullOrEmpty(deviceId) ? null : deviceId;
        ApplyOutputDevice();
    }

    private void ApplyOutputDevice()
    {
        if (_outputDeviceId is not null)
        {
            _mediaPlayer.SetOutputDevice(_outputDeviceId);
        }
    }

    public void Play() => _mediaPlayer.Play();

    public void Pause()
    {
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
        }
    }

    public void Stop() => _mediaPlayer.Stop();

    public void Seek(TimeSpan position) => _mediaPlayer.Time = (long)position.TotalMilliseconds;

    public TimeSpan Position
    {
        get
        {
            var time = _mediaPlayer.Time;
            return time < 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(time);
        }
    }

    public TimeSpan Duration
    {
        get
        {
            var duration = _mediaPlayer.Media?.Duration ?? -1;
            return duration < 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(duration);
        }
    }

    public float Volume
    {
        get => _mediaPlayer.Volume / 100f;
        set
        {
            var clamped = Math.Clamp(value, 0.0f, 1.0f);
            _mediaPlayer.Volume = (int)(clamped * 100);
        }
    }

    public float Rate
    {
        get => _mediaPlayer.Rate;
        set => _mediaPlayer.SetRate(value);
    }

    public bool IsPlaying => _mediaPlayer.State == VLCState.Playing;

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(e.Time));
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        PlaybackError?.Invoke(this, "LibVLCで再生エラーが発生しました。");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _mediaPlayer.TimeChanged -= OnTimeChanged;
        _mediaPlayer.EndReached -= OnEndReached;
        _mediaPlayer.EncounteredError -= OnEncounteredError;

        _media?.Dispose();
        _mediaPlayer.Dispose();
        _libVlc.Dispose();
    }
}
