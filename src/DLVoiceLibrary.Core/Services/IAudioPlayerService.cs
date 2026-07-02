namespace DLVoiceLibrary.Core.Services;

/// <summary>音声出力デバイス。Idは再生エンジン固有の識別子(空文字=システム既定)。</summary>
public sealed record AudioOutputDevice(string Id, string Name)
{
    // ComboBox の選択ボックス表示は DisplayMemberPath が効かないテンプレート経路を通ることが
    // あるため、ToString で表示名を返す
    public override string ToString() => Name;
}

/// <summary>音声再生エンジンの抽象化。実装(LibVLC等)はWPFアプリ側に置き、Coreはネイティブ依存を持たない。</summary>
public interface IAudioPlayerService : IDisposable
{
    void Load(string filePath);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);

    /// <summary>利用可能な音声出力デバイスの一覧(システム既定を含む)。</summary>
    IReadOnlyList<AudioOutputDevice> EnumerateOutputDevices();

    /// <summary>音声出力デバイスを切り替える。空文字またはnullでシステム既定。
    /// 再生中でも呼び出し可能で、以降ロードするメディアにも引き継がれる。</summary>
    void SetOutputDevice(string? deviceId);

    TimeSpan Position { get; }
    TimeSpan Duration { get; }

    /// <summary>0.0〜1.0</summary>
    float Volume { get; set; }

    /// <summary>再生速度。1.0が等速。</summary>
    float Rate { get; set; }

    bool IsPlaying { get; }

    /// <summary>再生位置が変化するたびに発火。実装はUIスレッド外から発火してよい(呼び出し側でマーシャリングする)。</summary>
    event EventHandler<TimeSpan>? PositionChanged;

    /// <summary>現在のトラックの再生が最後まで終わった時に発火(Stop()呼び出し等の明示的停止では発火しない)。</summary>
    event EventHandler? PlaybackEnded;

    event EventHandler<string>? PlaybackError;
}
