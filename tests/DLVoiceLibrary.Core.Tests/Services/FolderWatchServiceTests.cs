using DLVoiceLibrary.Core.Services;
using Xunit;

namespace DLVoiceLibrary.Core.Tests.Services;

public sealed class FolderWatchServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FolderWatchService _service;

    public FolderWatchServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"folderwatch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _service = new FolderWatchService();
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static async Task<string?> WaitForSingleNotificationAsync(FolderWatchService service, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<string> handler = (_, path) => tcs.TrySetResult(path);
        service.WorkFolderChanged += handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            return completed == tcs.Task ? await tcs.Task : null;
        }
        finally
        {
            service.WorkFolderChanged -= handler;
        }
    }

    [Fact]
    public async Task NewFolderCreatedDirectlyUnderRoot_RaisesWorkFolderChangedWithThatFolderPath()
    {
        _service.SetWatchFolders([_root]);

        var expectedPath = Path.Combine(_root, "work2");
        var waitTask = WaitForSingleNotificationAsync(_service, TimeSpan.FromSeconds(5));

        Directory.CreateDirectory(expectedPath);

        var notifiedPath = await waitTask;

        Assert.NotNull(notifiedPath);
        Assert.Equal(expectedPath, notifiedPath);
    }

    [Fact]
    public async Task NewFileDeepUnderExistingWorkFolder_RaisesWorkFolderChangedWithTopLevelFolderPath()
    {
        var workFolder = Directory.CreateDirectory(Path.Combine(_root, "work1")).FullName;
        var audioDir = Directory.CreateDirectory(Path.Combine(workFolder, "音声")).FullName;

        _service.SetWatchFolders([_root]);

        var waitTask = WaitForSingleNotificationAsync(_service, TimeSpan.FromSeconds(5));

        File.WriteAllText(Path.Combine(audioDir, "01.wav"), "dummy");

        var notifiedPath = await waitTask;

        Assert.NotNull(notifiedPath);
        Assert.Equal(workFolder, notifiedPath);
    }

    [Fact]
    public async Task MultipleFileCreationsInShortSuccession_DebouncedToSingleNotification()
    {
        var workFolder = Directory.CreateDirectory(Path.Combine(_root, "work1")).FullName;

        _service.SetWatchFolders([_root]);

        var receivedCount = 0;
        var lastReceivedPath = (string?)null;
        var gate = new object();
        _service.WorkFolderChanged += (_, path) =>
        {
            lock (gate)
            {
                receivedCount++;
                lastReceivedPath = path;
            }
        };

        for (var i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(workFolder, $"file{i}.wav"), "dummy");
            await Task.Delay(50);
        }

        // デバウンス(2秒)が確実に満了し、かつ複数回発火していないことを確認するために余裕を持って待つ。
        await Task.Delay(TimeSpan.FromSeconds(4));

        lock (gate)
        {
            Assert.Equal(1, receivedCount);
            Assert.Equal(workFolder, lastReceivedPath);
        }
    }

    [Fact]
    public void SetWatchFolders_WithNonExistentFolder_DoesNotThrowAndOtherFoldersStillWatched()
    {
        var nonExistent = Path.Combine(_root, "does_not_exist");
        var existing = Directory.CreateDirectory(Path.Combine(_root, "existing_root")).FullName;

        var exception = Record.Exception(() => _service.SetWatchFolders([nonExistent, existing]));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ValidFolderAmongNonExistentFolders_StillRaisesWorkFolderChanged()
    {
        var nonExistent = Path.Combine(_root, "does_not_exist");
        var existing = Directory.CreateDirectory(Path.Combine(_root, "existing_root")).FullName;

        _service.SetWatchFolders([nonExistent, existing]);

        var expectedPath = Path.Combine(existing, "workX");
        var waitTask = WaitForSingleNotificationAsync(_service, TimeSpan.FromSeconds(5));

        Directory.CreateDirectory(expectedPath);

        var notifiedPath = await waitTask;

        Assert.NotNull(notifiedPath);
        Assert.Equal(expectedPath, notifiedPath);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        _service.SetWatchFolders([_root]);

        var exception = Record.Exception(() =>
        {
            _service.Dispose();
            _service.Dispose();
            _service.Dispose();
        });

        Assert.Null(exception);
    }
}
