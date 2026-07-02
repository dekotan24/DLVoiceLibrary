using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;

namespace DLVoiceLibrary.ViewModels;

public enum MainTab
{
    Library,
    Playlists,
    Browser
}

public partial class MainViewModel : ObservableObject
{
    private readonly ILogService _log;

    public MainViewModel(LibraryViewModel library, PlaylistsViewModel playlists, PlayerBarViewModel player, ILogService log)
    {
        Library = library;
        Playlists = playlists;
        Player = player;
        _log = log;
    }

    public LibraryViewModel Library { get; }

    public PlaylistsViewModel Playlists { get; }

    public PlayerBarViewModel Player { get; }

    [ObservableProperty]
    private MainTab _currentTab = MainTab.Library;

    [RelayCommand]
    private void ShowLibraryTab() => CurrentTab = MainTab.Library;

    [RelayCommand]
    private async Task ShowPlaylistsTabAsync()
    {
        CurrentTab = MainTab.Playlists;
        await Playlists.LoadAsync();
    }

    [RelayCommand]
    private void ShowBrowserTab() => CurrentTab = MainTab.Browser;

    /// <summary>選択中の作品のDLsite商品ページURL。作品IDが無い場合はnull。</summary>
    public string? GetSelectedWorkDlsiteUrl()
    {
        var work = Library.WorkDetail.Work;
        if (work is null || string.IsNullOrEmpty(work.ProductId)) return null;

        var path = work.ProductId.StartsWith("VJ", StringComparison.OrdinalIgnoreCase) ? "pro" : "maniax";
        return $"https://www.dlsite.com/{path}/work/=/product_id/{work.ProductId}.html";
    }

    public async Task InitializeAsync()
    {
        await Library.LoadAsync();

        // 前回終了時に取得しきれなかったメタデータ(キューはメモリ内のため終了で消える)を拾い直す
        Library.EnqueuePendingMetadataFetches();

        await Player.RestoreLastSessionAsync();

        // 監視フォルダのリコンサイル(未登録フォルダの自動登録・既存作品の再スキャン)はawaitせず
        // 発火だけして起動をブロックしない。ObservableCollectionの更新はUIスレッドで行う必要があるため
        // Task.Runでスレッドプールに逃がさず、非同期のまま(UIスレッドのコンテキストを維持したまま)実行する。
        _ = RunWatchFolderReconcileAsync();
    }

    private async Task RunWatchFolderReconcileAsync()
    {
        try
        {
            await Library.RefreshWatchFoldersAsync();
        }
        catch (Exception ex)
        {
            _log.Error("監視フォルダの初期リコンサイルに失敗", ex);
        }
    }

    /// <summary>作品の全トラックを再生キューとして、指定トラックから再生を開始する(ライブラリのトラックツリーから)。</summary>
    public async Task PlayTrackAsync(Track track)
    {
        var work = Library.WorkDetail.Work;
        if (work is null || work.Id != track.WorkId)
        {
            _log.Warn($"再生対象トラックの作品が選択中の作品と一致しません: trackId={track.Id}");
            return;
        }

        await Player.PlayQueueAsync(work.Tracks, track, work.Title, work.ThumbnailPath);
    }

    /// <summary>選択中の作品を先頭トラックから全曲再生する。</summary>
    public async Task PlayWorkFromStartAsync()
    {
        var work = Library.WorkDetail.Work;
        if (work is null || work.Tracks.Count == 0) return;

        await Player.PlayQueueAsync(work.Tracks, work.Tracks[0], work.Title, work.ThumbnailPath);
    }

    /// <summary>選択中のプレイリストを指定トラックから再生する(作品横断キュー)。</summary>
    public async Task PlayPlaylistTrackAsync(Track startTrack)
    {
        var tracks = Playlists.SelectedPlaylistTracks;
        if (tracks.Count == 0) return;

        var playlistName = Playlists.SelectedPlaylist?.Name ?? "プレイリスト";
        await Player.PlayQueueAsync(tracks, startTrack, playlistName, thumbnailPath: string.Empty);
    }

    /// <summary>選択中のプレイリストを先頭トラックから全曲再生する。</summary>
    public async Task PlayPlaylistFromStartAsync()
    {
        var tracks = Playlists.SelectedPlaylistTracks;
        if (tracks.Count == 0) return;

        await PlayPlaylistTrackAsync(tracks[0]);
    }

    /// <summary>「最近再生した項目」ダイアログから、その一覧をキューとして指定トラックから再生する。</summary>
    public async Task PlayRecentTracksAsync(IReadOnlyList<Track> recentTracks, Track startTrack)
    {
        if (recentTracks.Count == 0) return;
        await Player.PlayQueueAsync(recentTracks, startTrack, "最近再生した項目", thumbnailPath: string.Empty);
    }
}
