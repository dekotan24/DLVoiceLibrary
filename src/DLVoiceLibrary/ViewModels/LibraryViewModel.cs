using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;
using DLVoiceLibrary.Scraping;

namespace DLVoiceLibrary.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly IDatabaseService _database;
    private readonly IFolderScanService _folderScan;
    private readonly IDlsiteScraperService _scraper;
    private readonly IFolderWatchService _folderWatch;
    private readonly HttpClient _thumbnailHttpClient;
    private readonly ILogService _log;
    private ICollectionView? _worksView;

    private readonly ConcurrentQueue<VoiceWork> _metadataQueue = new();
    private bool _metadataWorkerRunning;

    [ObservableProperty]
    private int _metadataQueueCount;

    [ObservableProperty]
    private bool _metadataFetchPaused;

    [RelayCommand]
    private void ToggleMetadataFetchPause() => MetadataFetchPaused = !MetadataFetchPaused;

    public LibraryViewModel(IDatabaseService database, IFolderScanService folderScan, IDlsiteScraperService scraper,
        IFolderWatchService folderWatch, HttpClient thumbnailHttpClient, ILogService log)
    {
        _database = database;
        _folderScan = folderScan;
        _scraper = scraper;
        _folderWatch = folderWatch;
        _thumbnailHttpClient = thumbnailHttpClient;
        _log = log;
        WorkDetail = new WorkDetailViewModel(database);
        _folderWatch.WorkFolderChanged += OnWorkFolderChanged;
    }

    public ObservableCollection<VoiceWork> Works { get; } = new();

    public WorkDetailViewModel WorkDetail { get; }

    public ICollectionView WorksView => _worksView ??= CreateView();

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => WorksView.Refresh();

    [ObservableProperty]
    private VoiceWork? _selectedWork;

    partial void OnSelectedWorkChanged(VoiceWork? value) => _ = LoadSelectedWorkDetailAsync(value);

    private async Task LoadSelectedWorkDetailAsync(VoiceWork? work)
    {
        if (work is null)
        {
            WorkDetail.Clear();
            return;
        }

        try
        {
            await WorkDetail.LoadAsync(work);
        }
        catch (Exception ex)
        {
            _log.Error($"トラック一覧の読み込みに失敗: {work.FolderPath}", ex);
        }
    }

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>手動削除された作品フォルダの除外リスト(メモリキャッシュ)。監視の自動登録をスキップするために使う。</summary>
    private HashSet<string> _excludedFolders = new(StringComparer.OrdinalIgnoreCase);

    public async Task LoadAsync()
    {
        var works = await _database.GetAllWorksAsync();
        Works.Clear();
        foreach (var work in works)
        {
            Works.Add(work);
        }
        _excludedFolders = await _database.GetExcludedFoldersAsync();
        StatusMessage = $"{Works.Count}件の作品";
    }

    private ICollectionView CreateView()
    {
        var view = CollectionViewSource.GetDefaultView(Works);
        view.Filter = FilterPredicate;
        return view;
    }

    // ---------- 詳細検索 ----------

    [ObservableProperty]
    private bool _isAdvancedSearchOpen;

    partial void OnIsAdvancedSearchOpenChanged(bool value)
    {
        if (value) UpdateFilterOptions();
    }

    [RelayCommand]
    private void ToggleAdvancedSearch() => IsAdvancedSearchOpen = !IsAdvancedSearchOpen;

    /// <summary>絞り込み解除を表すComboBoxの先頭項目。</summary>
    public const string NoSelection = "(指定なし)";

    public ObservableCollection<GenreFilterItem> GenreFilters { get; } = new();
    public ObservableCollection<string> CircleOptions { get; } = new();
    public ObservableCollection<GenreFilterItem> VoiceActorFilters { get; } = new();

    [ObservableProperty]
    private string _selectedCircle = NoSelection;

    partial void OnSelectedCircleChanged(string value) => RefreshFilter();

    /// <summary>お気に入り(★)を付けた作品のみ表示する。</summary>
    [ObservableProperty]
    private bool _favoritesOnly;

    partial void OnFavoritesOnlyChanged(bool value) => WorksView.Refresh();

    [ObservableProperty]
    private string _releaseDateFromText = string.Empty;

    partial void OnReleaseDateFromTextChanged(string value) => RefreshFilter();

    [ObservableProperty]
    private string _releaseDateToText = string.Empty;

    partial void OnReleaseDateToTextChanged(string value) => RefreshFilter();

    // FilterPredicateは作品ごとに呼ばれるため、選択状態はRefresh前に一度だけ集計してキャッシュする
    private List<string> _activeGenres = [];
    private List<string> _activeVoiceActors = [];
    private DateTime? _releaseFrom;
    private DateTime? _releaseTo;

    private void RefreshFilter()
    {
        _activeGenres = GenreFilters.Where(g => g.IsSelected).Select(g => g.Name).ToList();
        _activeVoiceActors = VoiceActorFilters.Where(v => v.IsSelected).Select(v => v.Name).ToList();
        _releaseFrom = DateTime.TryParse(ReleaseDateFromText, out var from) ? from : null;
        _releaseTo = DateTime.TryParse(ReleaseDateToText, out var to) ? to : null;
        WorksView.Refresh();
    }

    [RelayCommand]
    private void ClearAdvancedSearch()
    {
        foreach (var genre in GenreFilters)
        {
            genre.IsSelected = false;
        }
        foreach (var actor in VoiceActorFilters)
        {
            actor.IsSelected = false;
        }
        SelectedCircle = NoSelection;
        ReleaseDateFromText = string.Empty;
        ReleaseDateToText = string.Empty;
        RefreshFilter();
    }

    /// <summary>詳細検索の選択肢(ジャンル/サークル/声優)を現在のライブラリ内容から集計し直す。
    /// メタデータ取得の進行で増えるため、パネルを開くたびに呼ぶ。</summary>
    private void UpdateFilterOptions()
    {
        RebuildFilterChips(GenreFilters, Works.SelectMany(w => SplitCsv(w.GenreTags).Concat(SplitCsv(w.UserTags))));
        RebuildFilterChips(VoiceActorFilters, Works.SelectMany(w => SplitCsv(w.VoiceActors)));
        RebuildOptions(CircleOptions, Works.Select(w => w.CircleName), SelectedCircle, v => SelectedCircle = v);
    }

    /// <summary>チップ型フィルタ(タグ/声優)の選択肢を集計し直す。選択状態は名前で引き継ぐ。</summary>
    private void RebuildFilterChips(ObservableCollection<GenreFilterItem> target, IEnumerable<string> values)
    {
        var previouslySelected = target.Where(i => i.IsSelected).Select(i => i.Name).ToHashSet();

        var counts = values
            .GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        target.Clear();
        foreach (var group in counts)
        {
            target.Add(new GenreFilterItem(group.Key, group.Count(), RefreshFilter)
            {
                IsSelected = previouslySelected.Contains(group.Key)
            });
        }
    }

    private static void RebuildOptions(ObservableCollection<string> target, IEnumerable<string> values,
        string currentSelection, Action<string> restoreSelection)
    {
        var options = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        target.Clear();
        target.Add(NoSelection);
        foreach (var option in options)
        {
            target.Add(option);
        }

        restoreSelection(target.Contains(currentSelection) ? currentSelection : NoSelection);
    }

    private static string[] SplitCsv(string csv)
        => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private bool FilterPredicate(object obj)
    {
        if (obj is not VoiceWork work) return false;

        if (FavoritesOnly && !work.IsFavorite) return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim();
            var textMatch = work.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || work.CircleName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || work.ProductId.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || work.VoiceActors.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || work.UserTags.Contains(needle, StringComparison.OrdinalIgnoreCase);
            if (!textMatch) return false;
        }

        // 選択したジャンルタグ(DLsite由来+ユーザータグ)を全て含む作品のみ(AND検索)
        if (_activeGenres.Count > 0)
        {
            var workGenres = SplitCsv(work.GenreTags).Concat(SplitCsv(work.UserTags)).ToArray();
            if (!_activeGenres.All(g => workGenres.Contains(g, StringComparer.OrdinalIgnoreCase))) return false;
        }

        if (SelectedCircle != NoSelection
            && !string.Equals(work.CircleName, SelectedCircle, StringComparison.OrdinalIgnoreCase)) return false;

        // 選択した声優が全員出演している作品のみ(タグと同じAND検索)
        if (_activeVoiceActors.Count > 0)
        {
            var workActors = SplitCsv(work.VoiceActors);
            if (!_activeVoiceActors.All(a => workActors.Contains(a, StringComparer.OrdinalIgnoreCase))) return false;
        }

        // 販売日はDLsiteメタデータ取得済み作品のみ持つ。日付条件が指定されている場合、未取得(null)の作品は除外する
        if (_releaseFrom is { } from && (work.ReleaseDate is null || work.ReleaseDate < from)) return false;
        if (_releaseTo is { } to && (work.ReleaseDate is null || work.ReleaseDate > to)) return false;

        return true;
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "音声作品のフォルダを選択してください"
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        IsBusy = true;
        try
        {
            var added = await RegisterFolderAsync(dialog.SelectedPath);
            StatusMessage = added ? "1件を登録しました" : "既に登録済みか、登録に失敗しました";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddParentFolderAsync()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "複数の作品フォルダをまとめて含む親フォルダを選択してください"
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        IsBusy = true;
        try
        {
            var subfolders = Directory.GetDirectories(dialog.SelectedPath);
            var registered = 0;
            foreach (var folder in subfolders)
            {
                if (await RegisterFolderAsync(folder)) registered++;
            }
            StatusMessage = $"{registered}件を新規登録しました({subfolders.Length}件中)";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>フォルダをスキャンして作品として登録する。D&amp;D登録・手動登録・監視フォルダ自動登録の共通経路。</summary>
    /// <param name="folderPath">作品フォルダの絶対パス。</param>
    /// <param name="bypassExclusion">
    /// true(手動追加): 過去に手動削除された作品でも除外リストから外して登録し直す。
    /// false(監視による自動追加): 手動削除された作品フォルダはスキップする。
    /// </param>
    public async Task<bool> RegisterFolderAsync(string folderPath, bool bypassExclusion = true)
    {
        if (_excludedFolders.Contains(folderPath))
        {
            if (!bypassExclusion)
            {
                _log.Info($"手動削除された作品のため自動登録をスキップ: {folderPath}");
                return false;
            }

            await _database.RemoveExcludedFolderAsync(folderPath);
            _excludedFolders.Remove(folderPath);
        }

        var existing = await _database.GetWorkByFolderPathAsync(folderPath);
        if (existing is not null)
        {
            _log.Info($"既に登録済みのためスキップ: {folderPath}");
            return false;
        }

        var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var productIdMatch = _folderScan.ExtractProductId(folderName);

        var work = new VoiceWork
        {
            ProductId = productIdMatch?.ProductId ?? string.Empty,
            Source = productIdMatch is null ? "Manual" : productIdMatch.Source,
            Title = folderName,
            FolderPath = folderPath,
            RegisteredAt = DateTime.Now
        };

        long? insertedWorkId = null;
        try
        {
            var workId = await _database.InsertWorkAsync(work);
            insertedWorkId = workId;

            // タグ読み込みを含むスキャンは重い(数百ファイルで数秒)ため、UIスレッドを塞がないよう
            // スレッドプールで実行する。大量フォルダの一括登録時のUIフリーズ対策。
            var scanned = await Task.Run(() => _folderScan.ScanTracks(folderPath));
            foreach (var scannedTrack in scanned)
            {
                await _database.UpsertTrackAsync(new Track
                {
                    WorkId = workId,
                    FilePath = scannedTrack.FilePath,
                    TrackNo = scannedTrack.TrackNo,
                    Title = scannedTrack.Title,
                    Artist = scannedTrack.Artist,
                    DurationMs = scannedTrack.DurationMs,
                    FileFormat = scannedTrack.FileFormat,
                    AddedAt = DateTime.Now
                });
            }

            Works.Insert(0, work);
            _log.Info($"作品登録: {folderPath} (トラック{scanned.Count}件)");
            EnqueueMetadataFetch(work);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"作品登録に失敗: {folderPath}", ex);

            // 作品レコードだけ挿入されてスキャンで失敗した場合、トラック0件の幽霊レコードが残ると
            // 次回の一括追加やリコンサイルで「既に登録済み」としてスキップされ続けるため、削除しておく
            if (insertedWorkId is { } id)
            {
                try
                {
                    await _database.DeleteWorkAsync(id);
                }
                catch (Exception cleanupEx)
                {
                    _log.Error($"登録失敗した作品レコードの削除に失敗: {folderPath}", cleanupEx);
                }
            }
            return false;
        }
    }

    [RelayCommand]
    private async Task ToggleTrackFavoriteAsync(Track track)
    {
        track.IsFavorite = !track.IsFavorite;
        try
        {
            await _database.SetTrackFavoriteAsync(track.Id, track.IsFavorite);
        }
        catch (Exception ex)
        {
            _log.Error($"お気に入り状態の保存に失敗: track={track.Id}", ex);
        }
    }

    [RelayCommand]
    private async Task ToggleWorkFavoriteAsync(VoiceWork work)
    {
        work.IsFavorite = !work.IsFavorite;
        try
        {
            await _database.SetWorkFavoriteAsync(work.Id, work.IsFavorite);
        }
        catch (Exception ex)
        {
            _log.Error($"作品お気に入り状態の保存に失敗: work={work.Id}", ex);
        }

        // お気に入りのみ表示中に外した場合、一覧から即座に消す
        if (FavoritesOnly) WorksView.Refresh();
    }

    // ---------- 詳細パネルからのクリックフィルタ ----------

    /// <summary>作品詳細のサークル名クリック: そのサークルで絞り込む。</summary>
    [RelayCommand]
    private void FilterByCircle(string? circle)
    {
        if (string.IsNullOrWhiteSpace(circle)) return;
        UpdateFilterOptions();
        IsAdvancedSearchOpen = true;
        var trimmed = circle.Trim();
        SelectedCircle = CircleOptions.FirstOrDefault(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase)) ?? NoSelection;
    }

    /// <summary>作品詳細の声優名クリック: その声優を選択状態に追加して絞り込む。</summary>
    [RelayCommand]
    private void FilterByVoiceActor(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return;
        UpdateFilterOptions();
        IsAdvancedSearchOpen = true;
        SelectChip(VoiceActorFilters, actor.Trim());
    }

    /// <summary>作品詳細のタグクリック: そのタグを選択状態に追加して絞り込む。</summary>
    [RelayCommand]
    private void FilterByTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        UpdateFilterOptions();
        IsAdvancedSearchOpen = true;
        SelectChip(GenreFilters, tag.Trim());
    }

    private static void SelectChip(ObservableCollection<GenreFilterItem> chips, string name)
    {
        var chip = chips.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (chip is not null) chip.IsSelected = true;
    }

    [RelayCommand]
    private void RefetchMetadata()
    {
        var work = WorkDetail.Work;
        if (work is null || string.IsNullOrEmpty(work.ProductId)) return;
        EnqueueMetadataFetch(work);
    }

    /// <summary>作品をライブラリの一覧から削除する(ディスク上のファイルは削除しない)。
    /// 削除した作品は除外リストに入り、監視フォルダによる自動登録では復活しない。
    /// 手動追加(作品追加/一括追加/D&amp;D)すれば除外が解除されて再登録できる。</summary>
    public async Task DeleteWorkFromLibraryAsync(VoiceWork work)
    {
        try
        {
            await _database.DeleteWorkAsync(work.Id);
            await _database.AddExcludedFolderAsync(work.FolderPath);
            _excludedFolders.Add(work.FolderPath);

            if (SelectedWork == work) SelectedWork = null;
            Works.Remove(work);
            StatusMessage = $"「{work.Title}」を一覧から削除しました(フォルダ内のファイルは残っています)";
            _log.Info($"作品を一覧から削除(除外リスト追加): {work.FolderPath}");
        }
        catch (Exception ex)
        {
            _log.Error($"作品の削除に失敗: {work.FolderPath}", ex);
        }
    }

    /// <summary>
    /// メタデータ未取得のまま残っているDLsite作品を取得キューに再投入する。
    /// 取得キューはメモリ内にしか存在しないため、取得の途中でアプリを閉じると残りは失われる。
    /// そのため起動時に毎回これを呼び、未取得分(サークル名もサムネイルも無い作品)を拾い直す。
    /// </summary>
    public void EnqueuePendingMetadataFetches()
    {
        var pending = Works
            .Where(w => (w.Source == "DLsite" || w.Source == "FANZA")
                && !string.IsNullOrEmpty(w.ProductId)
                && (string.IsNullOrEmpty(w.CircleName) || string.IsNullOrEmpty(w.ThumbnailPath)))
            .ToList();

        if (pending.Count == 0) return;

        foreach (var work in pending)
        {
            EnqueueMetadataFetch(work);
        }
        _log.Info($"メタデータ未取得の{pending.Count}件を取得キューに再投入");
    }

    /// <summary>DLsite/FANZAの作品IDが判明している作品をバックグラウンドのメタデータ取得キューに投入する。
    /// サイト負荷軽減のため2秒間隔で1件ずつ順次処理する。</summary>
    private void EnqueueMetadataFetch(VoiceWork work)
    {
        if (string.IsNullOrEmpty(work.ProductId) || (work.Source != "DLsite" && work.Source != "FANZA")) return;

        _metadataQueue.Enqueue(work);
        MetadataQueueCount = _metadataQueue.Count;
        _ = EnsureMetadataWorkerRunningAsync();
    }

    /// <summary>DLsiteへの負荷・レート制限を避けるため2秒間隔で1件ずつ処理する。監視フォルダの一括登録等で
    /// 大量にキューが積まれた場合に備え、<see cref="MetadataFetchPaused"/>で一時停止できるようにしてある。</summary>
    private async Task EnsureMetadataWorkerRunningAsync()
    {
        if (_metadataWorkerRunning) return;
        _metadataWorkerRunning = true;

        try
        {
            while (!_metadataQueue.IsEmpty)
            {
                if (MetadataFetchPaused)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    continue;
                }

                if (!_metadataQueue.TryDequeue(out var work)) break;
                MetadataQueueCount = _metadataQueue.Count;

                await FetchAndApplyMetadataAsync(work);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
        finally
        {
            _metadataWorkerRunning = false;
            MetadataQueueCount = _metadataQueue.Count;
        }
    }

    private async Task FetchAndApplyMetadataAsync(VoiceWork work)
    {
        try
        {
            var metadata = await _scraper.FetchAsync(work.ProductId);
            if (metadata is null)
            {
                _log.Warn($"メタデータ取得に失敗(結果なし): {work.ProductId}");
                return;
            }

            work.Title = metadata.Title;
            work.CircleName = metadata.Circle;
            work.VoiceActors = string.Join(",", metadata.VoiceActors);
            work.GenreTags = string.Join(",", metadata.Genres);
            work.ReleaseDate = metadata.ReleaseDate;
            work.ThumbnailUrl = metadata.ThumbnailUrl;

            if (!string.IsNullOrEmpty(metadata.ThumbnailUrl))
            {
                work.ThumbnailPath = await DownloadThumbnailAsync(work.ProductId, metadata.ThumbnailUrl);
            }

            await _database.UpdateWorkAsync(work);
            StatusMessage = $"メタデータ取得完了: {work.Title}";
        }
        catch (Exception ex)
        {
            _log.Error($"メタデータ取得処理に失敗: {work.ProductId}", ex);
        }
    }

    private async Task<string> DownloadThumbnailAsync(string productId, string url)
    {
        try
        {
            var extension = Path.GetExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(extension)) extension = ".jpg";

            var path = Path.Combine(App.ThumbnailsDirectory, $"{productId}{extension}");
            if (File.Exists(path)) return path;

            var bytes = await _thumbnailHttpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch (Exception ex)
        {
            _log.Error($"サムネイルダウンロードに失敗: {url}", ex);
            return string.Empty;
        }
    }

    // ---------- 監視フォルダ ----------

    /// <summary>
    /// DBの監視フォルダ一覧を読み込み、①未登録フォルダの自動登録、②登録済み作品の新規ファイル拾い上げ(再スキャン)を
    /// 行った上で、実行中のリアルタイム監視(FileSystemWatcher)を最新の一覧で張り直す。
    /// アプリ起動時と、監視フォルダ設定ダイアログを閉じた後に呼ばれる。
    /// </summary>
    public async Task RefreshWatchFoldersAsync()
    {
        try
        {
            var watchFolders = await _database.GetWatchFoldersAsync();
            var watchFolderPaths = watchFolders.Select(w => w.FolderPath).ToList();

            var registeredPaths = Works.Select(w => w.FolderPath).ToHashSet();
            var newlyRegistered = 0;

            foreach (var root in watchFolderPaths)
            {
                var unregistered = WatchFolderReconciler.FindUnregisteredSubfolders(root, registeredPaths);
                foreach (var folder in unregistered)
                {
                    if (await RegisterFolderAsync(folder, bypassExclusion: false)) newlyRegistered++;
                }
            }

            foreach (var work in Works.ToList())
            {
                await RescanWorkTracksAsync(work);
            }

            _folderWatch.SetWatchFolders(watchFolderPaths);

            if (newlyRegistered > 0)
            {
                StatusMessage = $"監視フォルダから{newlyRegistered}件を自動登録しました";
            }
        }
        catch (Exception ex)
        {
            _log.Error("監視フォルダのリコンサイルに失敗", ex);
        }
    }

    /// <summary>既存登録済み作品のフォルダを再スキャンし、新規追加されたファイルをトラックとして拾い上げる。
    /// UpsertTrackAsyncはfile_path基準で冪等なため、既存トラックへの影響はない。</summary>
    private async Task RescanWorkTracksAsync(VoiceWork work)
    {
        if (!Directory.Exists(work.FolderPath)) return;

        try
        {
            var scanned = await Task.Run(() => _folderScan.ScanTracks(work.FolderPath));
            foreach (var scannedTrack in scanned)
            {
                await _database.UpsertTrackAsync(new Track
                {
                    WorkId = work.Id,
                    FilePath = scannedTrack.FilePath,
                    TrackNo = scannedTrack.TrackNo,
                    Title = scannedTrack.Title,
                    Artist = scannedTrack.Artist,
                    DurationMs = scannedTrack.DurationMs,
                    FileFormat = scannedTrack.FileFormat,
                    AddedAt = DateTime.Now
                });
            }

            if (WorkDetail.Work?.Id == work.Id)
            {
                await WorkDetail.LoadAsync(work);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"作品の再スキャンに失敗: {work.FolderPath}", ex);
        }
    }

    private void OnWorkFolderChanged(object? sender, string folderPath)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var existing = Works.FirstOrDefault(w => w.FolderPath == folderPath);
                if (existing is not null)
                {
                    await RescanWorkTracksAsync(existing);
                }
                else
                {
                    if (await RegisterFolderAsync(folderPath, bypassExclusion: false))
                    {
                        StatusMessage = $"監視フォルダから自動登録: {Path.GetFileName(folderPath)}";
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"監視フォルダの変化への対応に失敗: {folderPath}", ex);
            }
        });
    }
}
