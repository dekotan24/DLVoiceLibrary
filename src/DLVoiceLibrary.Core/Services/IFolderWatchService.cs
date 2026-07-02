namespace DLVoiceLibrary.Core.Services;

/// <summary>
/// 複数の監視フォルダ配下をFileSystemWatcherでリアルタイム監視する。起動時の未登録フォルダ検出(リコンサイル)は
/// 別途 <see cref="WatchFolderReconciler"/> の静的メソッドが担当し、このサービスは実行中の変化検知のみを扱う。
/// </summary>
public interface IFolderWatchService : IDisposable
{
    /// <summary>
    /// 監視対象の各ルートフォルダ直下にある「作品フォルダ相当のパス」に変化があった時に発火する。
    /// 新規作品フォルダの作成、既存作品フォルダ配下への新規ファイル追加のどちらでも、
    /// 影響を受ける直下の作品フォルダの絶対パスを引数として通知する(数秒間のデバウンス処理込み)。
    /// </summary>
    event EventHandler<string>? WorkFolderChanged;

    /// <summary>監視対象フォルダ一覧を(再)設定する。既存の監視は破棄して新しい一覧で張り直す。</summary>
    void SetWatchFolders(IReadOnlyList<string> rootFolders);
}
