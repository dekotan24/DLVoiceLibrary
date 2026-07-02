using CommunityToolkit.Mvvm.ComponentModel;

namespace DLVoiceLibrary.Core.Models;

public partial class Track : ObservableObject
{
    [ObservableProperty]
    private long _id;

    [ObservableProperty]
    private long _workId;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private int _trackNo;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _artist = string.Empty;

    [ObservableProperty]
    private long _durationMs;

    [ObservableProperty]
    private string _fileFormat = string.Empty;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private long _resumePositionMs;

    [ObservableProperty]
    private int _playCount;

    [ObservableProperty]
    private DateTime? _lastPlayedAt;

    [ObservableProperty]
    private DateTime _addedAt;

    /// <summary>作品のタイトル(横断プレイリスト表示用にキャッシュ)。DBカラムではない。</summary>
    [ObservableProperty]
    private string _workTitle = string.Empty;

    public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);
}
