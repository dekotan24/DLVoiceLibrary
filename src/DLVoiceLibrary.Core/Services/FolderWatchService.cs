namespace DLVoiceLibrary.Core.Services;

/// <summary>
/// <see cref="IFolderWatchService"/> の実装。監視ルートフォルダ配下の任意の深さで発生したファイル/フォルダ作成イベントを、
/// 「ルートフォルダ直下の作品フォルダパス」に正規化したうえで、短時間のデバウンスを挟んで通知する。
/// </summary>
public sealed class FolderWatchService : IFolderWatchService
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);
    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private readonly object _watchersLock = new();
    private readonly List<FileSystemWatcher> _watchers = [];

    private readonly object _timersLock = new();
    private readonly Dictionary<string, Timer> _debounceTimers = [];

    private bool _disposed;

    public event EventHandler<string>? WorkFolderChanged;

    public void SetWatchFolders(IReadOnlyList<string> rootFolders)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_watchersLock)
        {
            DisposeWatchersNoLock();

            foreach (var root in rootFolders)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    continue;
                }

                FileSystemWatcher watcher;
                try
                {
                    watcher = new FileSystemWatcher(root)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                    };
                }
                catch
                {
                    // フォルダへのアクセス権限がない等、監視開始自体に失敗した場合はそのフォルダだけ諦める。
                    continue;
                }

                var capturedRoot = root;
                watcher.Created += (_, e) => OnCreated(capturedRoot, e.FullPath);

                try
                {
                    watcher.EnableRaisingEvents = true;
                }
                catch
                {
                    watcher.Dispose();
                    continue;
                }

                _watchers.Add(watcher);
            }
        }
    }

    private void OnCreated(string root, string fullPath)
    {
        var workFolder = NormalizeToWorkFolder(root, fullPath);
        if (workFolder is null)
        {
            return;
        }

        ScheduleNotification(workFolder);
    }

    /// <summary>
    /// イベントのフルパスを、対応するルートフォルダ直下1階層目のパスに正規化する。
    /// ルート自体が変化した場合など、直下1階層目が特定できない場合はnullを返す。
    /// </summary>
    internal static string? NormalizeToWorkFolder(string root, string fullPath)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(root, fullPath);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrEmpty(relative) || relative == "." || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return null;
        }

        var firstSegment = relative.Split(PathSeparators, 2)[0];
        if (string.IsNullOrEmpty(firstSegment))
        {
            return null;
        }

        return Path.Combine(root, firstSegment);
    }

    private void ScheduleNotification(string workFolder)
    {
        lock (_timersLock)
        {
            if (_disposed)
            {
                return;
            }

            if (_debounceTimers.TryGetValue(workFolder, out var existingTimer))
            {
                existingTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
                return;
            }

            var timer = new Timer(_ => FireDebounced(workFolder), null, DebounceDelay, Timeout.InfiniteTimeSpan);
            _debounceTimers[workFolder] = timer;
        }
    }

    private void FireDebounced(string workFolder)
    {
        Timer? timer;
        lock (_timersLock)
        {
            if (!_debounceTimers.Remove(workFolder, out timer))
            {
                return;
            }
        }

        timer.Dispose();
        WorkFolderChanged?.Invoke(this, workFolder);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_watchersLock)
        {
            DisposeWatchersNoLock();
        }

        lock (_timersLock)
        {
            foreach (var timer in _debounceTimers.Values)
            {
                timer.Dispose();
            }

            _debounceTimers.Clear();
        }
    }

    private void DisposeWatchersNoLock()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}
