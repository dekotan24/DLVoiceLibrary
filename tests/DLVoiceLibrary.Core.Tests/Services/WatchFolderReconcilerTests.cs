using DLVoiceLibrary.Core.Services;
using Xunit;

namespace DLVoiceLibrary.Core.Tests.Services;

public sealed class WatchFolderReconcilerTests : IDisposable
{
    private readonly string _root;

    public WatchFolderReconcilerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"reconciler_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void FindUnregisteredSubfolders_ReturnsOnlyFoldersNotInRegisteredSet()
    {
        var folderA = Directory.CreateDirectory(Path.Combine(_root, "workA")).FullName;
        var folderB = Directory.CreateDirectory(Path.Combine(_root, "workB")).FullName;
        var folderC = Directory.CreateDirectory(Path.Combine(_root, "workC")).FullName;

        var registered = new HashSet<string> { folderA };

        var result = WatchFolderReconciler.FindUnregisteredSubfolders(_root, registered);

        Assert.DoesNotContain(folderA, result);
        Assert.Contains(folderB, result);
        Assert.Contains(folderC, result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FindUnregisteredSubfolders_AllRegistered_ReturnsEmpty()
    {
        var folderA = Directory.CreateDirectory(Path.Combine(_root, "workA")).FullName;
        var registered = new HashSet<string> { folderA };

        var result = WatchFolderReconciler.FindUnregisteredSubfolders(_root, registered);

        Assert.Empty(result);
    }

    [Fact]
    public void FindUnregisteredSubfolders_NoSubfolders_ReturnsEmpty()
    {
        var result = WatchFolderReconciler.FindUnregisteredSubfolders(_root, new HashSet<string>());
        Assert.Empty(result);
    }

    [Fact]
    public void FindUnregisteredSubfolders_RootDoesNotExist_ReturnsEmptyWithoutThrowing()
    {
        var missingRoot = Path.Combine(_root, "does_not_exist");
        var result = WatchFolderReconciler.FindUnregisteredSubfolders(missingRoot, new HashSet<string>());
        Assert.Empty(result);
    }
}
