using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace DLVoiceLibrary.Services;

/// <summary>
/// LAN内の他デバイスからブラウザでライブラリ閲覧・音声再生を行うWebメディアサーバ。
/// Kestrel (Minimal API) + WebUI/ の静的SPA。認証はユーザー名+パスワード → セッショントークン。
/// </summary>
public class WebServerService : IAsyncDisposable
{
    private readonly IDatabaseService _database;
    private readonly ILogService _log;
    private WebApplication? _app;

    private readonly ConcurrentDictionary<string, DateTime> _sessions = new();
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WebServerSettings Settings { get; private set; }

    public bool IsRunning => _app != null;
    public string? BaseUrl { get; private set; }

    public WebServerService(IDatabaseService database, ILogService log)
    {
        _database = database;
        _log = log;
        Settings = WebServerSettings.Load();
    }

    public async Task StartAsync()
    {
        if (_app != null) return;

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        var address = Settings.BindAll ? IPAddress.Any : IPAddress.Loopback;
        var port = Settings.Port;
        builder.WebHost.ConfigureKestrel(k => k.Listen(address, port));

        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            ctx.Response.Headers["Referrer-Policy"] = "same-origin";
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                _log.Error("Web API unhandled error.", ex);
                if (!ctx.Response.HasStarted)
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"error\":\"Internal server error\"}");
                }
            }
        });

        ConfigureApi(app);
        ConfigureStaticFiles(app);

        // RunAsync ではなく StartAsync を使う: bind 失敗 (ポート使用中等) の例外が呼び出し元に届く
        await app.StartAsync();
        _app = app;

        BaseUrl = Settings.BindAll ? $"http://{GetLanDisplayHost()}:{port}" : $"http://127.0.0.1:{port}";
        _log.Info($"Web media server started on {BaseUrl}");
    }

    public async Task StopAsync()
    {
        if (_app == null) return;
        try
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _app.StopAsync(stopCts.Token);
        }
        catch (Exception ex)
        {
            _log.Warn($"Web server stop error: {ex.Message}");
        }
        finally
        {
            await _app.DisposeAsync();
            _app = null;
            BaseUrl = null;
            _log.Info("Web media server stopped.");
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    // ------------------------------------------------------------------
    // ルーティング
    // ------------------------------------------------------------------

    private void ConfigureApi(WebApplication app)
    {
        // --- Auth ---
        app.MapPost("/api/auth/login", (Delegate)HandleLogin);
        app.MapPost("/api/auth/logout", (Delegate)HandleLogout);
        app.MapGet("/api/auth/status", (Delegate)HandleAuthStatus);

        // --- Library ---
        app.MapGet("/api/works", (Delegate)HandleGetWorks);
        app.MapGet("/api/works/{id:long}", (Delegate)HandleGetWork);
        app.MapGet("/api/works/{id:long}/thumbnail", (Delegate)HandleGetThumbnail);

        // --- Tracks ---
        app.MapGet("/api/tracks/{id:long}/stream", (Delegate)HandleStreamTrack);
        app.MapPost("/api/tracks/{id:long}/favorite", (Delegate)HandleToggleFavorite);
        app.MapPost("/api/tracks/{id:long}/played", (Delegate)HandleTrackPlayed);
        app.MapGet("/api/favorites", (Delegate)HandleGetFavorites);
        app.MapGet("/api/recent", (Delegate)HandleGetRecent);

        // --- Playlists ---
        app.MapGet("/api/playlists", (Delegate)HandleGetPlaylists);
        app.MapGet("/api/playlists/{id:long}/tracks", (Delegate)HandleGetPlaylistTracks);
    }

    private void ConfigureStaticFiles(WebApplication app)
    {
        var webUiPath = Path.Combine(AppContext.BaseDirectory, "WebUI");
        if (!Directory.Exists(webUiPath)) return;

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(webUiPath),
            RequestPath = ""
        });

        app.MapFallback(async ctx =>
        {
            var indexPath = Path.Combine(webUiPath, "index.html");
            if (File.Exists(indexPath))
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.SendFileAsync(indexPath);
            }
            else
            {
                ctx.Response.StatusCode = 404;
            }
        });
    }

    // ------------------------------------------------------------------
    // 認証
    // ------------------------------------------------------------------

    private bool IsAuthenticated(HttpContext ctx)
    {
        if (!Settings.HasPassword) return true;

        var token = ctx.Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "")
                    ?? ctx.Request.Cookies["dlvl_session"];
        if (string.IsNullOrEmpty(token)) return false;

        if (_sessions.TryGetValue(token, out var createdAt))
        {
            if (DateTime.UtcNow - createdAt < SessionTimeout) return true;
            _sessions.TryRemove(token, out _);
        }
        return false;
    }

    private IResult? RequireAuth(HttpContext ctx)
        => IsAuthenticated(ctx) ? null : Results.Json(new { error = "Unauthorized" }, JsonOpts, statusCode: 401);

    private record LoginRequest(string? Username, string? Password);

    private async Task<IResult> HandleLogin(HttpContext ctx)
    {
        var body = await ctx.Request.ReadFromJsonAsync<LoginRequest>();
        if (!Settings.HasPassword)
            return Results.Json(new { token = "", authRequired = false }, JsonOpts);

        if (body?.Username != Settings.Username || !Settings.VerifyPassword(body?.Password ?? ""))
            return Results.Json(new { error = "ユーザー名またはパスワードが違います" }, JsonOpts, statusCode: 401);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = DateTime.UtcNow;
        ctx.Response.Cookies.Append("dlvl_session", token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = SessionTimeout,
        });
        return Results.Json(new { token }, JsonOpts);
    }

    private IResult HandleLogout(HttpContext ctx)
    {
        var token = ctx.Request.Cookies["dlvl_session"];
        if (!string.IsNullOrEmpty(token)) _sessions.TryRemove(token, out _);
        ctx.Response.Cookies.Delete("dlvl_session");
        return Results.Json(new { ok = true }, JsonOpts);
    }

    private IResult HandleAuthStatus(HttpContext ctx)
        => Results.Json(new
        {
            authRequired = Settings.HasPassword,
            authenticated = IsAuthenticated(ctx),
        }, JsonOpts);

    // ------------------------------------------------------------------
    // ライブラリ
    // ------------------------------------------------------------------

    private async Task<IResult> HandleGetWorks(HttpContext ctx)
    {
        if (RequireAuth(ctx) is { } deny) return deny;

        var q = (ctx.Request.Query["q"].FirstOrDefault() ?? "").Trim();
        var sort = ctx.Request.Query["sort"].FirstOrDefault() ?? "registered-desc";

        var works = await _database.GetAllWorksAsync();
        var trackCounts = await _database.GetTrackCountsByWorkAsync();

        if (q.Length > 0)
        {
            var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            works = works.Where(w => terms.All(t =>
                w.Title.Contains(t, StringComparison.OrdinalIgnoreCase)
                || w.CircleName.Contains(t, StringComparison.OrdinalIgnoreCase)
                || w.VoiceActors.Contains(t, StringComparison.OrdinalIgnoreCase)
                || w.GenreTags.Contains(t, StringComparison.OrdinalIgnoreCase)
                || w.UserTags.Contains(t, StringComparison.OrdinalIgnoreCase)
                || w.ProductId.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        works = sort switch
        {
            "registered-asc" => works.OrderBy(w => w.RegisteredAt).ToList(),
            "release-desc" => works.OrderByDescending(w => w.ReleaseDate ?? DateTime.MinValue).ToList(),
            "release-asc" => works.OrderBy(w => w.ReleaseDate ?? DateTime.MaxValue).ToList(),
            "title-asc" => works.OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase).ToList(),
            "circle-asc" => works.OrderBy(w => w.CircleName, StringComparer.OrdinalIgnoreCase).ToList(),
            "played-desc" => works.OrderByDescending(w => w.LastPlayedAt ?? DateTime.MinValue).ToList(),
            _ => works.OrderByDescending(w => w.RegisteredAt).ToList(),
        };

        var results = works.Select(w => WorkSummary(w, trackCounts)).ToList();
        return Results.Json(new { total = results.Count, results }, JsonOpts);
    }

    private object WorkSummary(VoiceWork w, Dictionary<long, int> trackCounts) => new
    {
        id = w.Id,
        productId = w.ProductId,
        title = w.Title,
        circleName = w.CircleName,
        voiceActors = w.VoiceActorList.ToArray(),
        genreTags = w.GenreTagList.ToArray(),
        releaseDate = w.ReleaseDate?.ToString("yyyy-MM-dd"),
        rating = w.Rating,
        playCount = w.PlayCount,
        hasThumbnail = !string.IsNullOrEmpty(w.ThumbnailPath) && File.Exists(w.ThumbnailPath),
        trackCount = trackCounts.GetValueOrDefault(w.Id),
    };

    private async Task<IResult> HandleGetWork(long id, HttpContext ctx)
    {
        if (RequireAuth(ctx) is { } deny) return deny;

        var work = await _database.GetWorkByIdAsync(id);
        if (work == null) return Results.NotFound(new { error = "Work not found" });

        var tracks = await _database.GetTracksByWorkIdAsync(id);
        var trackCounts = new Dictionary<long, int> { [id] = tracks.Count };

        var trackDtos = tracks.Select(t => new
        {
            id = t.Id,
            title = t.Title,
            // フォルダ階層ツリーはフロントで relPath から構築する
            relPath = GetRelPathSafe(work.FolderPath, t.FilePath),
            durationMs = t.DurationMs,
            fileFormat = t.FileFormat,
            isFavorite = t.IsFavorite,
            playCount = t.PlayCount,
        }).ToList();

        return Results.Json(new
        {
            work = WorkSummary(work, trackCounts),
            memo = work.Memo,
            tracks = trackDtos,
        }, JsonOpts);
    }

    private static string GetRelPathSafe(string basePath, string filePath)
    {
        try
        {
            var rel = Path.GetRelativePath(basePath, filePath);
            return rel.StartsWith("..") ? Path.GetFileName(filePath) : rel.Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(filePath);
        }
    }

    private async Task<IResult> HandleGetThumbnail(long id, HttpContext ctx)
    {
        if (RequireAuth(ctx) is { } deny) return deny;

        var work = await _database.GetWorkByIdAsync(id);
        if (work == null || string.IsNullOrEmpty(work.ThumbnailPath) || !File.Exists(work.ThumbnailPath))
            return Results.NotFound();

        var ext = Path.GetExtension(work.ThumbnailPath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
        return Results.File(work.ThumbnailPath, contentType);
    }

    // ------------------------------------------------------------------
    // トラック
    // ------------------------------------------------------------------

    private async Task<IResult> HandleStreamTrack(long id, HttpContext ctx)
    {
        if (RequireAuth(ctx) is { } deny) return deny;

        var track = await _database.GetTrackByIdAsync(id);
        if (track == null) return Results.NotFound(new { error = "Track not found" });
        if (!File.Exists(track.FilePath)) return Results.NotFound(new { error = "File not found on disk" });

        var ext = Path.GetExtension(track.FilePath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            _ => "application/octet-stream"
        };
        // enableRangeProcessing: シーク (Range リクエスト) 対応に必須
        return Results.File(track.FilePath, contentType, enableRangeProcessing: true);
    }

    private async Task<IResult> HandleToggleFavorite(long id, HttpContext ctx)
    {
        if (RequireAuth(ctx) is { } deny) return deny;

        var track = await _database.GetTrackByIdAsync(id);
        if (track == null) return Results.NotFound(new { error = "Track not found" });

        var newValue = !track.IsFavorite;
        await _database.SetTrackFavoriteAsync(id, newValue);
        return Results.Json(new { favorite = newValue }, JsonOpts);
    }

    /// <summary>Web側での再生をアプリの再生履歴・再生回数に反映する</summary>
    private async Task<IResult> HandleTrackPlayed(long id, HttpContext ctx)
    {
        if (RequireAuth(ctx) is { } deny) return deny;

        var track = await _database.GetTrackByIdAsync(id);
        if (track == null) return Results.NotFound(new { error = "Track not found" });

        var now = DateTime.Now;
        await _database.AddPlayHistoryAsync(id, now);
        await _database.UpdateTrackPlaybackStateAsync(id, track.ResumePositionMs, track.PlayCount + 1, now);
        return Results.Json(new { ok = true }, JsonOpts);
    }

    private async Task<IResult> HandleGetFavorites(HttpContext ctx)
    {
        if (RequireAuth(ctx) is { } deny) return deny;
        var tracks = await _database.GetFavoriteTracksAsync();
        return Results.Json(new { results = tracks.Select(TrackWithWork).ToList() }, JsonOpts);
    }

    private async Task<IResult> HandleGetRecent(HttpContext ctx)
    {
        if (RequireAuth(ctx) is { } deny) return deny;
        var tracks = await _database.GetRecentlyPlayedTracksAsync(100);
        return Results.Json(new { results = tracks.Select(TrackWithWork).ToList() }, JsonOpts);
    }

    private static object TrackWithWork(Track t) => new
    {
        id = t.Id,
        workId = t.WorkId,
        title = t.Title,
        workTitle = t.WorkTitle,
        durationMs = t.DurationMs,
        fileFormat = t.FileFormat,
        isFavorite = t.IsFavorite,
        playCount = t.PlayCount,
    };

    // ------------------------------------------------------------------
    // プレイリスト
    // ------------------------------------------------------------------

    private async Task<IResult> HandleGetPlaylists(HttpContext ctx)
    {
        if (RequireAuth(ctx) is { } deny) return deny;

        var playlists = await _database.GetAllPlaylistsAsync();
        var results = new List<object>();
        foreach (var p in playlists)
        {
            var tracks = await _database.GetPlaylistTracksAsync(p.Id);
            results.Add(new
            {
                id = p.Id,
                name = p.Name,
                trackCount = tracks.Count,
                totalDurationMs = tracks.Sum(t => t.DurationMs),
            });
        }
        return Results.Json(new { results }, JsonOpts);
    }

    private async Task<IResult> HandleGetPlaylistTracks(long id, HttpContext ctx)
    {
        if (RequireAuth(ctx) is { } deny) return deny;
        var tracks = await _database.GetPlaylistTracksAsync(id);
        return Results.Json(new { results = tracks.Select(TrackWithWork).ToList() }, JsonOpts);
    }

    // ------------------------------------------------------------------
    // ヘルパ
    // ------------------------------------------------------------------

    /// <summary>表示用のLAN IPを返す。0.0.0.0はアクセス先として使えないため実IPを出す (192.168系優先)</summary>
    private static string GetLanDisplayHost()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                          && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                          && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .Select(a => a.ToString())
                .ToList();

            return candidates.FirstOrDefault(ip => ip.StartsWith("192.168."))
                ?? candidates.FirstOrDefault(ip => ip.StartsWith("10."))
                ?? candidates.FirstOrDefault()
                ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
