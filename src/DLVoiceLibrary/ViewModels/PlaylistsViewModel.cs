using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;

namespace DLVoiceLibrary.ViewModels;

public partial class PlaylistsViewModel : ObservableObject
{
    private readonly IDatabaseService _database;
    private readonly ILogService _log;

    public PlaylistsViewModel(IDatabaseService database, ILogService log)
    {
        _database = database;
        _log = log;
    }

    public ObservableCollection<Playlist> Playlists { get; } = new();

    public ObservableCollection<Track> SelectedPlaylistTracks { get; } = new();

    [ObservableProperty]
    private Playlist? _selectedPlaylist;

    partial void OnSelectedPlaylistChanged(Playlist? value) => _ = LoadPlaylistTracksAsync(value);

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public async Task LoadAsync()
    {
        var playlists = await _database.GetAllPlaylistsAsync();
        var previouslySelectedId = SelectedPlaylist?.Id;

        Playlists.Clear();
        foreach (var playlist in playlists)
        {
            Playlists.Add(playlist);
        }

        SelectedPlaylist = previouslySelectedId is { } id
            ? Playlists.FirstOrDefault(p => p.Id == id)
            : Playlists.FirstOrDefault();
    }

    private async Task LoadPlaylistTracksAsync(Playlist? playlist)
    {
        SelectedPlaylistTracks.Clear();
        if (playlist is null) return;

        try
        {
            var tracks = await _database.GetPlaylistTracksAsync(playlist.Id);
            foreach (var track in tracks)
            {
                SelectedPlaylistTracks.Add(track);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"プレイリストのトラック読み込みに失敗: {playlist.Id}", ex);
        }
    }

    public async Task<Playlist> CreatePlaylistAsync(string name)
    {
        var id = await _database.CreatePlaylistAsync(name.Trim());
        await LoadAsync();
        var created = Playlists.First(p => p.Id == id);
        SelectedPlaylist = created;
        return created;
    }

    [RelayCommand]
    private async Task RenameSelectedPlaylistAsync(string newName)
    {
        if (SelectedPlaylist is null || string.IsNullOrWhiteSpace(newName)) return;

        await _database.RenamePlaylistAsync(SelectedPlaylist.Id, newName.Trim());
        SelectedPlaylist.Name = newName.Trim();
    }

    [RelayCommand]
    private async Task DeleteSelectedPlaylistAsync()
    {
        if (SelectedPlaylist is null) return;

        await _database.DeletePlaylistAsync(SelectedPlaylist.Id);
        var removed = SelectedPlaylist;
        SelectedPlaylist = null;
        Playlists.Remove(removed);
        SelectedPlaylistTracks.Clear();
    }

    /// <summary>作品横断で任意のトラックをプレイリストに追加する。作品ライブラリ側のトラックツリーから呼ばれる。</summary>
    public async Task AddTrackToPlaylistAsync(long playlistId, Track track)
    {
        await _database.AddTrackToPlaylistAsync(playlistId, track.Id);
        if (SelectedPlaylist?.Id == playlistId)
        {
            await LoadPlaylistTracksAsync(SelectedPlaylist);
        }
    }

    [RelayCommand]
    private async Task RemoveTrackAsync(Track track)
    {
        if (SelectedPlaylist is null) return;

        await _database.RemoveTrackFromPlaylistAsync(SelectedPlaylist.Id, track.Id);
        SelectedPlaylistTracks.Remove(track);
    }

    [RelayCommand]
    private async Task MoveTrackUpAsync(Track track)
    {
        var index = SelectedPlaylistTracks.IndexOf(track);
        if (index <= 0) return;

        SelectedPlaylistTracks.Move(index, index - 1);
        await PersistOrderAsync();
    }

    [RelayCommand]
    private async Task MoveTrackDownAsync(Track track)
    {
        var index = SelectedPlaylistTracks.IndexOf(track);
        if (index < 0 || index >= SelectedPlaylistTracks.Count - 1) return;

        SelectedPlaylistTracks.Move(index, index + 1);
        await PersistOrderAsync();
    }

    private async Task PersistOrderAsync()
    {
        if (SelectedPlaylist is null) return;

        var orderedIds = SelectedPlaylistTracks.Select(t => t.Id).ToList();
        await _database.ReorderPlaylistAsync(SelectedPlaylist.Id, orderedIds);
    }
}
