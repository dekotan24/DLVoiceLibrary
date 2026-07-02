using DLVoiceLibrary.Core.Models;

namespace DLVoiceLibrary.Core.Services;

/// <summary>
/// 再生キューの純粋ロジック(音声エンジンに一切依存しない)。作品全曲再生・プレイリスト再生どちらの
/// キューもこのサービスで表現する。再生位置(秒数)に依存する判断(前トラックへ戻る/曲頭に戻る等)は
/// 呼び出し側(PlayerBarViewModel)の責務とし、このサービスはインデックス operationのみを扱う。
/// </summary>
public interface IPlaybackQueueService
{
    /// <summary>現在の再生順(シャッフル中はシャッフル後の順序)。</summary>
    IReadOnlyList<Track> Tracks { get; }

    int CurrentIndex { get; }

    Track? CurrentTrack { get; }

    RepeatMode RepeatMode { get; set; }

    bool ShuffleOn { get; }

    bool HasNext { get; }

    bool HasPrevious { get; }

    /// <summary>新しいキューを読み込む。startTrackを指定するとそのトラックから開始する(見つからなければ先頭)。</summary>
    void LoadQueue(IReadOnlyList<Track> tracks, Track? startTrack = null);

    /// <summary>
    /// シャッフルのON/OFFを切り替える。ONにする時は現在トラックを先頭に固定して残りをシャッフルする。
    /// OFFにする時は元の順序へ復元し、現在トラックのインデックスを再計算する。
    /// </summary>
    void SetShuffle(bool on);

    /// <summary>
    /// 次のトラックへ進む。RepeatMode.Oneの場合は同じトラックを返す(呼び出し側が曲頭からの再生を担当)。
    /// キュー末尾に達した場合、RepeatMode.Allなら先頭に戻り、Offならnullを返す(キュー終了)。
    /// </summary>
    Track? MoveNext();

    /// <summary>
    /// 前のトラックへ戻る。キュー先頭にいる場合、RepeatMode.Allなら末尾へ、それ以外はnullを返す。
    /// </summary>
    Track? MovePrevious();
}
