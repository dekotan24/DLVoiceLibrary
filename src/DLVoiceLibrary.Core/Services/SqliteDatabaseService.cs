using DLVoiceLibrary.Core.Models;
using Microsoft.Data.Sqlite;

namespace DLVoiceLibrary.Core.Services;

public sealed class SqliteDatabaseService : IDatabaseService
{
    private readonly string _connectionString;

    private static readonly string[] Migrations =
    [
        // v1: 初期スキーマ
        """
        CREATE TABLE voice_works (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_id TEXT NOT NULL DEFAULT '',
            source TEXT NOT NULL DEFAULT '',
            title TEXT NOT NULL DEFAULT '',
            circle_name TEXT NOT NULL DEFAULT '',
            voice_actors TEXT NOT NULL DEFAULT '',
            genre_tags TEXT NOT NULL DEFAULT '',
            release_date TEXT,
            folder_path TEXT NOT NULL,
            thumbnail_path TEXT NOT NULL DEFAULT '',
            thumbnail_url TEXT NOT NULL DEFAULT '',
            registered_at TEXT NOT NULL,
            last_played_at TEXT,
            play_count INTEGER NOT NULL DEFAULT 0,
            rating INTEGER NOT NULL DEFAULT 0,
            memo TEXT NOT NULL DEFAULT '',
            UNIQUE(folder_path)
        );

        CREATE TABLE tracks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            work_id INTEGER NOT NULL REFERENCES voice_works(id) ON DELETE CASCADE,
            file_path TEXT NOT NULL,
            track_no INTEGER NOT NULL DEFAULT 0,
            title TEXT NOT NULL DEFAULT '',
            artist TEXT NOT NULL DEFAULT '',
            duration_ms INTEGER NOT NULL DEFAULT 0,
            file_format TEXT NOT NULL DEFAULT '',
            is_favorite INTEGER NOT NULL DEFAULT 0,
            resume_position_ms INTEGER NOT NULL DEFAULT 0,
            play_count INTEGER NOT NULL DEFAULT 0,
            last_played_at TEXT,
            added_at TEXT NOT NULL,
            UNIQUE(file_path)
        );
        CREATE INDEX idx_tracks_work_id ON tracks(work_id);
        CREATE INDEX idx_tracks_favorite ON tracks(is_favorite);

        CREATE TABLE playlists (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE playlist_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
            track_id INTEGER NOT NULL REFERENCES tracks(id) ON DELETE CASCADE,
            position INTEGER NOT NULL
        );
        CREATE UNIQUE INDEX idx_playlist_items_order ON playlist_items(playlist_id, position);

        CREATE TABLE play_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            track_id INTEGER NOT NULL REFERENCES tracks(id) ON DELETE CASCADE,
            played_at TEXT NOT NULL
        );
        CREATE INDEX idx_play_history_track ON play_history(track_id);
        CREATE INDEX idx_play_history_played_at ON play_history(played_at);

        CREATE TABLE watch_folders (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            folder_path TEXT NOT NULL UNIQUE,
            added_at TEXT NOT NULL
        );

        CREATE TABLE app_state (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            last_track_id INTEGER REFERENCES tracks(id) ON DELETE SET NULL,
            last_position_ms INTEGER NOT NULL DEFAULT 0,
            volume REAL NOT NULL DEFAULT 1.0,
            repeat_mode TEXT NOT NULL DEFAULT 'Off',
            shuffle_on INTEGER NOT NULL DEFAULT 0,
            playback_rate REAL NOT NULL DEFAULT 1.0
        );
        INSERT INTO app_state (id) VALUES (1);
        """,

        // v2: ①ライブラリから手動削除した作品を監視フォルダの自動登録から除外するためのリスト
        //     (手動追加(作品追加/一括追加/D&D)された場合はこのリストから取り除いて再登録を許可する)。
        //     ②ユーザーが自由に付けられるタグ(DLsite由来のgenre_tagsとは別管理)。
        //     ③音声出力デバイスの選択(空文字=システム既定)。
        """
        CREATE TABLE excluded_folders (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            folder_path TEXT NOT NULL UNIQUE,
            excluded_at TEXT NOT NULL
        );
        ALTER TABLE voice_works ADD COLUMN user_tags TEXT NOT NULL DEFAULT '';
        ALTER TABLE app_state ADD COLUMN audio_device_id TEXT NOT NULL DEFAULT '';
        """
    ];

    public SqliteDatabaseService(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using (var pragmaCmd = connection.CreateCommand())
        {
            // UI操作とバックグラウンドのスクレイピング/スキャン/再生位置保存が並行して書き込むため、
            // 競合時に即SQLITE_BUSYで落ちず一定時間待つようにする(WALモードと併用)。
            pragmaCmd.CommandText = "PRAGMA busy_timeout=5000;";
            await pragmaCmd.ExecuteNonQueryAsync(ct);
        }
        return connection;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);

        await using (var walCmd = connection.CreateCommand())
        {
            // WALはDBファイルに永続する設定。読み取りと書き込みの並行実行を可能にする。
            walCmd.CommandText = "PRAGMA journal_mode=WAL;";
            await walCmd.ExecuteNonQueryAsync(ct);
        }

        var currentVersion = await GetUserVersionAsync(connection, ct);
        for (var version = currentVersion; version < Migrations.Length; version++)
        {
            await using var tx = connection.BeginTransaction();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = Migrations[version];
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await SetUserVersionAsync(connection, tx, version + 1, ct);
            await tx.CommitAsync(ct);
        }
    }

    private static async Task<int> GetUserVersionAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static async Task SetUserVersionAsync(SqliteConnection connection, SqliteTransaction tx, int version, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA user_version = {version};";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ---------- Works ----------

    public async Task<List<VoiceWork>> GetAllWorksAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM voice_works ORDER BY registered_at DESC;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<VoiceWork>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapWork(reader));
        }
        return results;
    }

    public async Task<VoiceWork?> GetWorkByIdAsync(long workId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM voice_works WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", workId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapWork(reader) : null;
    }

    public async Task<VoiceWork?> GetWorkByFolderPathAsync(string folderPath, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM voice_works WHERE folder_path = @folderPath;";
        cmd.Parameters.AddWithValue("@folderPath", folderPath);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapWork(reader) : null;
    }

    public async Task<long> InsertWorkAsync(VoiceWork work, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO voice_works
                (product_id, source, title, circle_name, voice_actors, genre_tags, release_date,
                 folder_path, thumbnail_path, thumbnail_url, registered_at, last_played_at,
                 play_count, rating, memo, user_tags)
            VALUES
                (@productId, @source, @title, @circleName, @voiceActors, @genreTags, @releaseDate,
                 @folderPath, @thumbnailPath, @thumbnailUrl, @registeredAt, @lastPlayedAt,
                 @playCount, @rating, @memo, @userTags);
            SELECT last_insert_rowid();
            """;
        BindWorkParameters(cmd, work);
        var id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        work.Id = id;
        return id;
    }

    public async Task UpdateWorkAsync(VoiceWork work, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE voice_works SET
                product_id = @productId, source = @source, title = @title, circle_name = @circleName,
                voice_actors = @voiceActors, genre_tags = @genreTags, release_date = @releaseDate,
                folder_path = @folderPath, thumbnail_path = @thumbnailPath, thumbnail_url = @thumbnailUrl,
                registered_at = @registeredAt, last_played_at = @lastPlayedAt, play_count = @playCount,
                rating = @rating, memo = @memo, user_tags = @userTags
            WHERE id = @id;
            """;
        BindWorkParameters(cmd, work);
        cmd.Parameters.AddWithValue("@id", work.Id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteWorkAsync(long workId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM voice_works WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", workId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ---------- 除外フォルダ(手動削除した作品を監視の自動登録から守るリスト) ----------

    public async Task AddExcludedFolderAsync(string folderPath, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO excluded_folders (folder_path, excluded_at) VALUES (@folderPath, @excludedAt)
            ON CONFLICT(folder_path) DO NOTHING;
            """;
        cmd.Parameters.AddWithValue("@folderPath", folderPath);
        cmd.Parameters.AddWithValue("@excludedAt", DateTime.Now.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveExcludedFolderAsync(string folderPath, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM excluded_folders WHERE folder_path = @folderPath;";
        cmd.Parameters.AddWithValue("@folderPath", folderPath);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<HashSet<string>> GetExcludedFoldersAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT folder_path FROM excluded_folders;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetString(0));
        }
        return results;
    }

    private static void BindWorkParameters(SqliteCommand cmd, VoiceWork work)
    {
        cmd.Parameters.AddWithValue("@productId", work.ProductId);
        cmd.Parameters.AddWithValue("@source", work.Source);
        cmd.Parameters.AddWithValue("@title", work.Title);
        cmd.Parameters.AddWithValue("@circleName", work.CircleName);
        cmd.Parameters.AddWithValue("@voiceActors", work.VoiceActors);
        cmd.Parameters.AddWithValue("@genreTags", work.GenreTags);
        cmd.Parameters.AddWithValue("@releaseDate", (object?)work.ReleaseDate?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@folderPath", work.FolderPath);
        cmd.Parameters.AddWithValue("@thumbnailPath", work.ThumbnailPath);
        cmd.Parameters.AddWithValue("@thumbnailUrl", work.ThumbnailUrl);
        cmd.Parameters.AddWithValue("@registeredAt", work.RegisteredAt.ToString("O"));
        cmd.Parameters.AddWithValue("@lastPlayedAt", (object?)work.LastPlayedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@playCount", work.PlayCount);
        cmd.Parameters.AddWithValue("@rating", work.Rating);
        cmd.Parameters.AddWithValue("@memo", work.Memo);
        cmd.Parameters.AddWithValue("@userTags", work.UserTags);
    }

    private static VoiceWork MapWork(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        ProductId = reader.GetString(reader.GetOrdinal("product_id")),
        Source = reader.GetString(reader.GetOrdinal("source")),
        Title = reader.GetString(reader.GetOrdinal("title")),
        CircleName = reader.GetString(reader.GetOrdinal("circle_name")),
        VoiceActors = reader.GetString(reader.GetOrdinal("voice_actors")),
        GenreTags = reader.GetString(reader.GetOrdinal("genre_tags")),
        ReleaseDate = ReadNullableDate(reader, "release_date"),
        FolderPath = reader.GetString(reader.GetOrdinal("folder_path")),
        ThumbnailPath = reader.GetString(reader.GetOrdinal("thumbnail_path")),
        ThumbnailUrl = reader.GetString(reader.GetOrdinal("thumbnail_url")),
        RegisteredAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("registered_at")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        LastPlayedAt = ReadNullableDate(reader, "last_played_at"),
        PlayCount = reader.GetInt32(reader.GetOrdinal("play_count")),
        Rating = reader.GetInt32(reader.GetOrdinal("rating")),
        Memo = reader.GetString(reader.GetOrdinal("memo")),
        UserTags = reader.GetString(reader.GetOrdinal("user_tags"))
    };

    private static DateTime? ReadNullableDate(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return null;
        return DateTime.Parse(reader.GetString(ordinal), null, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    // ---------- Tracks ----------

    public async Task<List<Track>> GetTracksByWorkIdAsync(long workId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        // track_noはFolderScanServiceがファイル名の自然順で採番したもの。file_pathの辞書順ソートは
        // 「track1, track10, track2...」のように壊れるため使わない(track_noが同値の場合のみtie-break)。
        cmd.CommandText = "SELECT * FROM tracks WHERE work_id = @workId ORDER BY track_no, file_path;";
        cmd.Parameters.AddWithValue("@workId", workId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Track>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapTrack(reader));
        }
        return results;
    }

    public async Task<Track?> GetTrackByFilePathAsync(string filePath, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM tracks WHERE file_path = @filePath;";
        cmd.Parameters.AddWithValue("@filePath", filePath);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapTrack(reader) : null;
    }

    public async Task<Track?> GetTrackByIdAsync(long trackId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM tracks WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", trackId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapTrack(reader) : null;
    }

    public async Task<long> UpsertTrackAsync(Track track, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO tracks (work_id, file_path, track_no, title, artist, duration_ms, file_format, added_at)
            VALUES (@workId, @filePath, @trackNo, @title, @artist, @durationMs, @fileFormat, @addedAt)
            ON CONFLICT(file_path) DO UPDATE SET
                work_id = excluded.work_id,
                track_no = excluded.track_no,
                title = excluded.title,
                artist = excluded.artist,
                duration_ms = excluded.duration_ms,
                file_format = excluded.file_format
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("@workId", track.WorkId);
        cmd.Parameters.AddWithValue("@filePath", track.FilePath);
        cmd.Parameters.AddWithValue("@trackNo", track.TrackNo);
        cmd.Parameters.AddWithValue("@title", track.Title);
        cmd.Parameters.AddWithValue("@artist", track.Artist);
        cmd.Parameters.AddWithValue("@durationMs", track.DurationMs);
        cmd.Parameters.AddWithValue("@fileFormat", track.FileFormat);
        cmd.Parameters.AddWithValue("@addedAt", track.AddedAt == default ? DateTime.Now.ToString("O") : track.AddedAt.ToString("O"));
        var id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        track.Id = id;
        return id;
    }

    public async Task UpdateTrackPlaybackStateAsync(long trackId, long resumePositionMs, int playCount, DateTime lastPlayedAt, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE tracks SET resume_position_ms = @resumePositionMs, play_count = @playCount, last_played_at = @lastPlayedAt
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@resumePositionMs", resumePositionMs);
        cmd.Parameters.AddWithValue("@playCount", playCount);
        cmd.Parameters.AddWithValue("@lastPlayedAt", lastPlayedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@id", trackId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetTrackFavoriteAsync(long trackId, bool isFavorite, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE tracks SET is_favorite = @isFavorite WHERE id = @id;";
        cmd.Parameters.AddWithValue("@isFavorite", isFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", trackId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<Track>> GetFavoriteTracksAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM tracks WHERE is_favorite = 1 ORDER BY title;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Track>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapTrack(reader));
        }
        return results;
    }

    private static Track MapTrack(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        WorkId = reader.GetInt64(reader.GetOrdinal("work_id")),
        FilePath = reader.GetString(reader.GetOrdinal("file_path")),
        TrackNo = reader.GetInt32(reader.GetOrdinal("track_no")),
        Title = reader.GetString(reader.GetOrdinal("title")),
        Artist = reader.GetString(reader.GetOrdinal("artist")),
        DurationMs = reader.GetInt64(reader.GetOrdinal("duration_ms")),
        FileFormat = reader.GetString(reader.GetOrdinal("file_format")),
        IsFavorite = reader.GetInt32(reader.GetOrdinal("is_favorite")) != 0,
        ResumePositionMs = reader.GetInt64(reader.GetOrdinal("resume_position_ms")),
        PlayCount = reader.GetInt32(reader.GetOrdinal("play_count")),
        LastPlayedAt = ReadNullableDate(reader, "last_played_at"),
        AddedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("added_at")), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };

    // ---------- Playlists ----------

    public async Task<List<Playlist>> GetAllPlaylistsAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM playlists ORDER BY updated_at DESC;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Playlist>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new Playlist
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")), null, System.Globalization.DateTimeStyles.RoundtripKind),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at")), null, System.Globalization.DateTimeStyles.RoundtripKind)
            });
        }
        return results;
    }

    public async Task<long> CreatePlaylistAsync(string name, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        var now = DateTime.Now.ToString("O");
        cmd.CommandText = """
            INSERT INTO playlists (name, created_at, updated_at) VALUES (@name, @now, @now);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@now", now);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task RenamePlaylistAsync(long playlistId, string name, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET name = @name, updated_at = @now WHERE id = @id;";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("@id", playlistId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeletePlaylistAsync(long playlistId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM playlists WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", playlistId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<Track>> GetPlaylistTracksAsync(long playlistId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT t.*, w.title AS work_title
            FROM playlist_items pi
            JOIN tracks t ON t.id = pi.track_id
            JOIN voice_works w ON w.id = t.work_id
            WHERE pi.playlist_id = @playlistId
            ORDER BY pi.position;
            """;
        cmd.Parameters.AddWithValue("@playlistId", playlistId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Track>();
        while (await reader.ReadAsync(ct))
        {
            var track = MapTrack(reader);
            track.WorkTitle = reader.GetString(reader.GetOrdinal("work_title"));
            results.Add(track);
        }
        return results;
    }

    public async Task AddTrackToPlaylistAsync(long playlistId, long trackId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO playlist_items (playlist_id, track_id, position)
            VALUES (@playlistId, @trackId, (SELECT COALESCE(MAX(position), -1) + 1 FROM playlist_items WHERE playlist_id = @playlistId));
            UPDATE playlists SET updated_at = @now WHERE id = @playlistId;
            """;
        cmd.Parameters.AddWithValue("@playlistId", playlistId);
        cmd.Parameters.AddWithValue("@trackId", trackId);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveTrackFromPlaylistAsync(long playlistId, long trackId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var tx = connection.BeginTransaction();

        long removedPosition;
        await using (var selectCmd = connection.CreateCommand())
        {
            selectCmd.Transaction = tx;
            selectCmd.CommandText = "SELECT position FROM playlist_items WHERE playlist_id = @playlistId AND track_id = @trackId;";
            selectCmd.Parameters.AddWithValue("@playlistId", playlistId);
            selectCmd.Parameters.AddWithValue("@trackId", trackId);
            var result = await selectCmd.ExecuteScalarAsync(ct);
            if (result is null)
            {
                await tx.CommitAsync(ct);
                return;
            }
            removedPosition = (long)result;
        }

        await using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM playlist_items WHERE playlist_id = @playlistId AND track_id = @trackId;";
            deleteCmd.Parameters.AddWithValue("@playlistId", playlistId);
            deleteCmd.Parameters.AddWithValue("@trackId", trackId);
            await deleteCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var shiftCmd = connection.CreateCommand())
        {
            shiftCmd.Transaction = tx;
            shiftCmd.CommandText = """
                UPDATE playlist_items SET position = position - 1
                WHERE playlist_id = @playlistId AND position > @removedPosition;
                """;
            shiftCmd.Parameters.AddWithValue("@playlistId", playlistId);
            shiftCmd.Parameters.AddWithValue("@removedPosition", removedPosition);
            await shiftCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task ReorderPlaylistAsync(long playlistId, IReadOnlyList<long> trackIdsInOrder, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var tx = connection.BeginTransaction();

        // 一時的に大きな値へ退避してからUNIQUE(playlist_id, position)との衝突を避けて再採番する
        await using (var offsetCmd = connection.CreateCommand())
        {
            offsetCmd.Transaction = tx;
            offsetCmd.CommandText = "UPDATE playlist_items SET position = position + 1000000 WHERE playlist_id = @playlistId;";
            offsetCmd.Parameters.AddWithValue("@playlistId", playlistId);
            await offsetCmd.ExecuteNonQueryAsync(ct);
        }

        for (var i = 0; i < trackIdsInOrder.Count; i++)
        {
            await using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = "UPDATE playlist_items SET position = @position WHERE playlist_id = @playlistId AND track_id = @trackId;";
            updateCmd.Parameters.AddWithValue("@position", i);
            updateCmd.Parameters.AddWithValue("@playlistId", playlistId);
            updateCmd.Parameters.AddWithValue("@trackId", trackIdsInOrder[i]);
            await updateCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    // ---------- Play history ----------

    public async Task AddPlayHistoryAsync(long trackId, DateTime playedAt, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO play_history (track_id, played_at) VALUES (@trackId, @playedAt);";
        cmd.Parameters.AddWithValue("@trackId", trackId);
        cmd.Parameters.AddWithValue("@playedAt", playedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<Track>> GetRecentlyPlayedTracksAsync(int limit, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT t.*, w.title AS work_title, MAX(ph.played_at) AS last_played
            FROM play_history ph
            JOIN tracks t ON t.id = ph.track_id
            JOIN voice_works w ON w.id = t.work_id
            GROUP BY t.id
            ORDER BY last_played DESC
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Track>();
        while (await reader.ReadAsync(ct))
        {
            var track = MapTrack(reader);
            track.WorkTitle = reader.GetString(reader.GetOrdinal("work_title"));
            results.Add(track);
        }
        return results;
    }

    // ---------- Watch folders ----------

    public async Task<List<WatchFolder>> GetWatchFoldersAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM watch_folders ORDER BY added_at;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<WatchFolder>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new WatchFolder(
                reader.GetInt64(reader.GetOrdinal("id")),
                reader.GetString(reader.GetOrdinal("folder_path")),
                DateTime.Parse(reader.GetString(reader.GetOrdinal("added_at")), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }
        return results;
    }

    public async Task AddWatchFolderAsync(string folderPath, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO watch_folders (folder_path, added_at) VALUES (@folderPath, @addedAt);";
        cmd.Parameters.AddWithValue("@folderPath", folderPath);
        cmd.Parameters.AddWithValue("@addedAt", DateTime.Now.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveWatchFolderAsync(long watchFolderId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM watch_folders WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", watchFolderId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ---------- App state ----------

    public async Task<AppState> GetAppStateAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM app_state WHERE id = 1;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return new AppState();
        }

        var lastTrackOrdinal = reader.GetOrdinal("last_track_id");
        return new AppState
        {
            LastTrackId = reader.IsDBNull(lastTrackOrdinal) ? null : reader.GetInt64(lastTrackOrdinal),
            LastPositionMs = reader.GetInt64(reader.GetOrdinal("last_position_ms")),
            Volume = reader.GetDouble(reader.GetOrdinal("volume")),
            RepeatMode = Enum.Parse<RepeatMode>(reader.GetString(reader.GetOrdinal("repeat_mode"))),
            ShuffleOn = reader.GetInt32(reader.GetOrdinal("shuffle_on")) != 0,
            PlaybackRate = reader.GetDouble(reader.GetOrdinal("playback_rate")),
            AudioDeviceId = reader.GetString(reader.GetOrdinal("audio_device_id"))
        };
    }

    public async Task SaveAppStateAsync(AppState state, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE app_state SET
                last_track_id = @lastTrackId, last_position_ms = @lastPositionMs, volume = @volume,
                repeat_mode = @repeatMode, shuffle_on = @shuffleOn, playback_rate = @playbackRate,
                audio_device_id = @audioDeviceId
            WHERE id = 1;
            """;
        cmd.Parameters.AddWithValue("@lastTrackId", (object?)state.LastTrackId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lastPositionMs", state.LastPositionMs);
        cmd.Parameters.AddWithValue("@volume", state.Volume);
        cmd.Parameters.AddWithValue("@repeatMode", state.RepeatMode.ToString());
        cmd.Parameters.AddWithValue("@shuffleOn", state.ShuffleOn ? 1 : 0);
        cmd.Parameters.AddWithValue("@playbackRate", state.PlaybackRate);
        cmd.Parameters.AddWithValue("@audioDeviceId", state.AudioDeviceId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
