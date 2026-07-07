using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DLVoiceLibrary.Core.Models;

public partial class VoiceWork : ObservableObject
{
    [ObservableProperty]
    private long _id;

    [ObservableProperty]
    private string _productId = string.Empty;

    [ObservableProperty]
    private string _source = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _circleName = string.Empty;

    [ObservableProperty]
    private string _voiceActors = string.Empty;

    [ObservableProperty]
    private string _genreTags = string.Empty;

    [ObservableProperty]
    private DateTime? _releaseDate;

    [ObservableProperty]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    private string _thumbnailPath = string.Empty;

    [ObservableProperty]
    private string _thumbnailUrl = string.Empty;

    [ObservableProperty]
    private DateTime _registeredAt;

    [ObservableProperty]
    private DateTime? _lastPlayedAt;

    [ObservableProperty]
    private int _playCount;

    [ObservableProperty]
    private int _rating;

    [ObservableProperty]
    private string _memo = string.Empty;

    /// <summary>ユーザーが自由に付けるタグ(カンマ区切り)。DLsite由来のGenreTagsとは別管理で、再取得しても上書きされない。</summary>
    [ObservableProperty]
    private string _userTags = string.Empty;

    /// <summary>作品単位のお気に入り。トラック単位のTrack.IsFavoriteとは独立。</summary>
    [ObservableProperty]
    private bool _isFavorite;

    public ObservableCollection<Track> Tracks { get; } = new();

    public IEnumerable<string> VoiceActorList =>
        VoiceActors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public IEnumerable<string> GenreTagList =>
        GenreTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // メタデータ再取得で元のCSVが変わったとき、リスト表示(ItemsControl)側にも変更を伝える
    partial void OnVoiceActorsChanged(string value) => OnPropertyChanged(nameof(VoiceActorList));
    partial void OnGenreTagsChanged(string value) => OnPropertyChanged(nameof(GenreTagList));
}
