using DLVoiceLibrary.Core.Models;

namespace DLVoiceLibrary.Core.Services;

public interface IDatabaseService
{
    Task InitializeAsync(CancellationToken ct = default);

    // Works
    Task<List<VoiceWork>> GetAllWorksAsync(CancellationToken ct = default);
    Task<VoiceWork?> GetWorkByIdAsync(long workId, CancellationToken ct = default);
    Task<VoiceWork?> GetWorkByFolderPathAsync(string folderPath, CancellationToken ct = default);
    Task<long> InsertWorkAsync(VoiceWork work, CancellationToken ct = default);
    Task UpdateWorkAsync(VoiceWork work, CancellationToken ct = default);
    Task DeleteWorkAsync(long workId, CancellationToken ct = default);

    // Tracks
    Task<List<Track>> GetTracksByWorkIdAsync(long workId, CancellationToken ct = default);
    Task<Track?> GetTrackByFilePathAsync(string filePath, CancellationToken ct = default);
    Task<Track?> GetTrackByIdAsync(long trackId, CancellationToken ct = default);

    /// <summary>file_pathのUNIQUE制約に基づき挿入または更新する。戻り値はトラックID。</summary>
    Task<long> UpsertTrackAsync(Track track, CancellationToken ct = default);

    Task UpdateTrackPlaybackStateAsync(long trackId, long resumePositionMs, int playCount, DateTime lastPlayedAt, CancellationToken ct = default);
    Task SetTrackFavoriteAsync(long trackId, bool isFavorite, CancellationToken ct = default);
    Task<List<Track>> GetFavoriteTracksAsync(CancellationToken ct = default);

    // Playlists
    Task<List<Playlist>> GetAllPlaylistsAsync(CancellationToken ct = default);
    Task<long> CreatePlaylistAsync(string name, CancellationToken ct = default);
    Task RenamePlaylistAsync(long playlistId, string name, CancellationToken ct = default);
    Task DeletePlaylistAsync(long playlistId, CancellationToken ct = default);
    Task<List<Track>> GetPlaylistTracksAsync(long playlistId, CancellationToken ct = default);
    Task AddTrackToPlaylistAsync(long playlistId, long trackId, CancellationToken ct = default);
    Task RemoveTrackFromPlaylistAsync(long playlistId, long trackId, CancellationToken ct = default);
    Task ReorderPlaylistAsync(long playlistId, IReadOnlyList<long> trackIdsInOrder, CancellationToken ct = default);

    // Play history
    Task AddPlayHistoryAsync(long trackId, DateTime playedAt, CancellationToken ct = default);
    Task<List<Track>> GetRecentlyPlayedTracksAsync(int limit, CancellationToken ct = default);

    // Watch folders
    Task<List<WatchFolder>> GetWatchFoldersAsync(CancellationToken ct = default);
    Task AddWatchFolderAsync(string folderPath, CancellationToken ct = default);
    Task RemoveWatchFolderAsync(long watchFolderId, CancellationToken ct = default);

    // Excluded folders (手動削除した作品を監視の自動登録から守るリスト)
    Task AddExcludedFolderAsync(string folderPath, CancellationToken ct = default);
    Task RemoveExcludedFolderAsync(string folderPath, CancellationToken ct = default);
    Task<HashSet<string>> GetExcludedFoldersAsync(CancellationToken ct = default);

    // App state
    Task<AppState> GetAppStateAsync(CancellationToken ct = default);
    Task SaveAppStateAsync(AppState state, CancellationToken ct = default);
}
