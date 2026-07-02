using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DLVoiceLibrary.Core.Tests.Services;

public sealed class DatabaseServiceTests : IAsyncLifetime, IDisposable
{
    private readonly string _dbPath;
    private SqliteDatabaseService _db = null!;

    public DatabaseServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dlvl_test_{Guid.NewGuid():N}.db");
    }

    public async Task InitializeAsync()
    {
        _db = new SqliteDatabaseService(_dbPath);
        await _db.InitializeAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static VoiceWork MakeWork(string folderPath, string title = "テスト作品") => new()
    {
        ProductId = "RJ01234567",
        Source = "DLsite",
        Title = title,
        CircleName = "テストサークル",
        FolderPath = folderPath,
        RegisteredAt = DateTime.Now
    };

    private static Track MakeTrack(long workId, string filePath, int trackNo = 1) => new()
    {
        WorkId = workId,
        FilePath = filePath,
        TrackNo = trackNo,
        Title = $"トラック{trackNo}",
        FileFormat = "mp3",
        AddedAt = DateTime.Now
    };

    [Fact]
    public async Task InsertWork_ThenGetByFolderPath_ReturnsWork()
    {
        var work = MakeWork(@"C:\voice\work1");
        var id = await _db.InsertWorkAsync(work);

        var fetched = await _db.GetWorkByFolderPathAsync(@"C:\voice\work1");

        Assert.NotNull(fetched);
        Assert.Equal(id, fetched!.Id);
        Assert.Equal("テスト作品", fetched.Title);
    }

    [Fact]
    public async Task InsertWork_DuplicateFolderPath_ThrowsUniqueConstraintViolation()
    {
        await _db.InsertWorkAsync(MakeWork(@"C:\voice\dup"));

        await Assert.ThrowsAsync<SqliteException>(() => _db.InsertWorkAsync(MakeWork(@"C:\voice\dup")));
    }

    [Fact]
    public async Task UpdateWork_ChangesPersist()
    {
        var work = MakeWork(@"C:\voice\work2");
        await _db.InsertWorkAsync(work);

        work.Title = "更新後タイトル";
        work.Rating = 5;
        await _db.UpdateWorkAsync(work);

        var fetched = await _db.GetWorkByIdAsync(work.Id);
        Assert.Equal("更新後タイトル", fetched!.Title);
        Assert.Equal(5, fetched.Rating);
    }

    [Fact]
    public async Task DeleteWork_CascadesToTracks()
    {
        var work = MakeWork(@"C:\voice\work3");
        var workId = await _db.InsertWorkAsync(work);
        await _db.UpsertTrackAsync(MakeTrack(workId, @"C:\voice\work3\01.mp3"));
        await _db.UpsertTrackAsync(MakeTrack(workId, @"C:\voice\work3\02.mp3", 2));

        await _db.DeleteWorkAsync(workId);

        var tracks = await _db.GetTracksByWorkIdAsync(workId);
        Assert.Empty(tracks);
    }

    [Fact]
    public async Task UpsertTrack_SameFilePath_UpdatesInsteadOfDuplicating()
    {
        var workId = await _db.InsertWorkAsync(MakeWork(@"C:\voice\work4"));
        var filePath = @"C:\voice\work4\01.mp3";

        var firstId = await _db.UpsertTrackAsync(MakeTrack(workId, filePath, 1));
        var track2 = MakeTrack(workId, filePath, 1);
        track2.Title = "改題後";
        var secondId = await _db.UpsertTrackAsync(track2);

        Assert.Equal(firstId, secondId);
        var tracks = await _db.GetTracksByWorkIdAsync(workId);
        Assert.Single(tracks);
        Assert.Equal("改題後", tracks[0].Title);
    }

    [Fact]
    public async Task UpsertTrack_SameFilePathDifferentWork_ReassignsOwnership()
    {
        // 入れ子フォルダ(例: 特典として同梱された別作品)のファイルが、後から別作品として
        // 再登録された場合に所有権(work_id)が正しく移ることを確認する。
        var workAId = await _db.InsertWorkAsync(MakeWork(@"C:\voice\parent"));
        var workBId = await _db.InsertWorkAsync(MakeWork(@"C:\voice\parent\bonus", "特典作品"));
        var sharedPath = @"C:\voice\parent\bonus\01.mp3";

        await _db.UpsertTrackAsync(MakeTrack(workAId, sharedPath));
        await _db.UpsertTrackAsync(MakeTrack(workBId, sharedPath));

        var tracksUnderA = await _db.GetTracksByWorkIdAsync(workAId);
        var tracksUnderB = await _db.GetTracksByWorkIdAsync(workBId);
        Assert.Empty(tracksUnderA);
        Assert.Single(tracksUnderB);
    }

    [Fact]
    public async Task SetTrackFavorite_ReflectedInGetFavoriteTracks()
    {
        var workId = await _db.InsertWorkAsync(MakeWork(@"C:\voice\work5"));
        var trackId = await _db.UpsertTrackAsync(MakeTrack(workId, @"C:\voice\work5\01.mp3"));

        await _db.SetTrackFavoriteAsync(trackId, true);

        var favorites = await _db.GetFavoriteTracksAsync();
        Assert.Contains(favorites, t => t.Id == trackId);
    }

    [Fact]
    public async Task PlaylistReordering_UpdatesPositionsCorrectly()
    {
        var workId = await _db.InsertWorkAsync(MakeWork(@"C:\voice\work6"));
        var t1 = await _db.UpsertTrackAsync(MakeTrack(workId, @"C:\voice\work6\01.mp3", 1));
        var t2 = await _db.UpsertTrackAsync(MakeTrack(workId, @"C:\voice\work6\02.mp3", 2));
        var t3 = await _db.UpsertTrackAsync(MakeTrack(workId, @"C:\voice\work6\03.mp3", 3));

        var playlistId = await _db.CreatePlaylistAsync("横断プレイリスト");
        await _db.AddTrackToPlaylistAsync(playlistId, t1);
        await _db.AddTrackToPlaylistAsync(playlistId, t2);
        await _db.AddTrackToPlaylistAsync(playlistId, t3);

        await _db.ReorderPlaylistAsync(playlistId, [t3, t1, t2]);

        var tracks = await _db.GetPlaylistTracksAsync(playlistId);
        Assert.Equal([t3, t1, t2], tracks.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task RemoveTrackFromPlaylist_ClosesGapInPositions()
    {
        var workId = await _db.InsertWorkAsync(MakeWork(@"C:\voice\work7"));
        var t1 = await _db.UpsertTrackAsync(MakeTrack(workId, @"C:\voice\work7\01.mp3", 1));
        var t2 = await _db.UpsertTrackAsync(MakeTrack(workId, @"C:\voice\work7\02.mp3", 2));
        var t3 = await _db.UpsertTrackAsync(MakeTrack(workId, @"C:\voice\work7\03.mp3", 3));

        var playlistId = await _db.CreatePlaylistAsync("プレイリスト2");
        await _db.AddTrackToPlaylistAsync(playlistId, t1);
        await _db.AddTrackToPlaylistAsync(playlistId, t2);
        await _db.AddTrackToPlaylistAsync(playlistId, t3);

        await _db.RemoveTrackFromPlaylistAsync(playlistId, t2);

        var tracks = await _db.GetPlaylistTracksAsync(playlistId);
        Assert.Equal([t1, t3], tracks.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task DeletePlaylist_DoesNotDeleteUnderlyingTracks()
    {
        var workId = await _db.InsertWorkAsync(MakeWork(@"C:\voice\work8"));
        var trackId = await _db.UpsertTrackAsync(MakeTrack(workId, @"C:\voice\work8\01.mp3"));
        var playlistId = await _db.CreatePlaylistAsync("消えるプレイリスト");
        await _db.AddTrackToPlaylistAsync(playlistId, trackId);

        await _db.DeletePlaylistAsync(playlistId);

        var track = await _db.GetTrackByIdAsync(trackId);
        Assert.NotNull(track);
    }

    [Fact]
    public async Task AppState_SaveAndLoad_RoundTrips()
    {
        var workId = await _db.InsertWorkAsync(MakeWork(@"C:\voice\work9"));
        var trackId = await _db.UpsertTrackAsync(MakeTrack(workId, @"C:\voice\work9\01.mp3"));

        var state = new AppState
        {
            LastTrackId = trackId,
            LastPositionMs = 12345,
            Volume = 0.8,
            RepeatMode = RepeatMode.All,
            ShuffleOn = true,
            PlaybackRate = 1.25
        };
        await _db.SaveAppStateAsync(state);

        var loaded = await _db.GetAppStateAsync();
        Assert.Equal(trackId, loaded.LastTrackId);
        Assert.Equal(12345, loaded.LastPositionMs);
        Assert.Equal(0.8, loaded.Volume);
        Assert.Equal(RepeatMode.All, loaded.RepeatMode);
        Assert.True(loaded.ShuffleOn);
        Assert.Equal(1.25, loaded.PlaybackRate);
    }

    [Fact]
    public async Task WatchFolders_AddAndRemove_Work()
    {
        await _db.AddWatchFolderAsync(@"N:\extracted\doujin\voice");
        await _db.AddWatchFolderAsync(@"N:\extracted\doujin\voice"); // 重複は無視される

        var folders = await _db.GetWatchFoldersAsync();
        Assert.Single(folders);

        await _db.RemoveWatchFolderAsync(folders[0].Id);
        Assert.Empty(await _db.GetWatchFoldersAsync());
    }

    [Fact]
    public async Task ExcludedFolders_AddRemoveAndDuplicate_Work()
    {
        await _db.AddExcludedFolderAsync(@"C:\voice\deleted_work");
        await _db.AddExcludedFolderAsync(@"C:\voice\deleted_work"); // 重複は無視される(ON CONFLICT DO NOTHING)

        var excluded = await _db.GetExcludedFoldersAsync();
        Assert.Single(excluded);
        Assert.Contains(@"C:\voice\deleted_work", excluded);

        await _db.RemoveExcludedFolderAsync(@"C:\voice\deleted_work");
        Assert.Empty(await _db.GetExcludedFoldersAsync());
    }

    [Fact]
    public async Task UserTags_PersistAcrossInsertAndUpdate()
    {
        var work = MakeWork(@"C:\voice\tagged_work");
        work.UserTags = "お気に入り,睡眠用";
        var workId = await _db.InsertWorkAsync(work);

        var loaded = await _db.GetWorkByIdAsync(workId);
        Assert.Equal("お気に入り,睡眠用", loaded!.UserTags);

        loaded.UserTags = "作業用";
        await _db.UpdateWorkAsync(loaded);

        var reloaded = await _db.GetWorkByIdAsync(workId);
        Assert.Equal("作業用", reloaded!.UserTags);
    }

    [Fact]
    public async Task AppState_AudioDeviceId_RoundTrips()
    {
        var state = new AppState { AudioDeviceId = @"{0.0.0.00000000}.{abc-123}" };
        await _db.SaveAppStateAsync(state);

        var loaded = await _db.GetAppStateAsync();
        Assert.Equal(@"{0.0.0.00000000}.{abc-123}", loaded.AudioDeviceId);
    }

    [Fact]
    public async Task ForeignKeys_ArePragmaEnabled()
    {
        // 外部キー制約が有効なら、存在しないwork_idへの挿入は失敗するはず
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            ForeignKeys = true
        }.ToString());
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO tracks (work_id, file_path, added_at) VALUES (99999, 'x.mp3', @now);";
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("O"));

        await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
    }
}
