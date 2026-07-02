namespace DLVoiceLibrary.Core.Services;

/// <summary>
/// アプリ起動時に監視フォルダ直下を走査し、まだDBに登録されていない作品フォルダを見つける純粋ロジック。
/// アプリが起動していない間に追加されたフォルダを拾い上げるための処理(既存登録済み作品の新規ファイル拾い上げは
/// FolderScanServiceの再スキャン+UpsertTrackAsyncの冪等性で別途対応する)。
/// </summary>
public static class WatchFolderReconciler
{
    public static IReadOnlyList<string> FindUnregisteredSubfolders(string watchFolderRoot, IReadOnlySet<string> registeredFolderPaths)
    {
        if (!Directory.Exists(watchFolderRoot))
        {
            return [];
        }

        return Directory.GetDirectories(watchFolderRoot)
            .Where(folder => !registeredFolderPaths.Contains(folder))
            .ToList();
    }
}
