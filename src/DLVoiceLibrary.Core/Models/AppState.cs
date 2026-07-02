namespace DLVoiceLibrary.Core.Models;

public sealed class AppState
{
    public long? LastTrackId { get; set; }
    public long LastPositionMs { get; set; }
    public double Volume { get; set; } = 1.0;
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;
    public bool ShuffleOn { get; set; }
    public double PlaybackRate { get; set; } = 1.0;

    /// <summary>音声出力デバイスID(空文字=システム既定)。</summary>
    public string AudioDeviceId { get; set; } = string.Empty;
}
