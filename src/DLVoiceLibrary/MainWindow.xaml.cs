using System.IO;
using System.Windows;
using System.Windows.Controls;
using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;
using DLVoiceLibrary.Scraping;
using DLVoiceLibrary.Services;
using DLVoiceLibrary.ViewModels;
using DLVoiceLibrary.Views;

namespace DLVoiceLibrary;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly LibVlcAudioPlayerService _audioPlayer;
    private bool _shutdownInProgress;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        var app = (App)Application.Current;
        var tagReader = new TagLibTagReaderService();
        var folderScan = new FolderScanService(tagReader);
        var scraper = new DlsiteScraperService();
        var folderWatch = new FolderWatchService();
        var library = new LibraryViewModel(app.Database, folderScan, scraper, folderWatch, app.DlsiteHttpClient, app.LogService);
        var playlists = new PlaylistsViewModel(app.Database, app.LogService);

        _audioPlayer = new LibVlcAudioPlayerService();
        var queue = new PlaybackQueueService();
        var player = new PlayerBarViewModel(_audioPlayer, queue, app.Database, app.LogService);

        _viewModel = new MainViewModel(library, playlists, player, app.LogService);
        DataContext = _viewModel;

        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closing += OnWindowClosing;
        PreviewKeyDown += OnWindowPreviewKeyDown;

        // WebView2の作業フォルダをアプリのデータフォルダに固定する
        // (未指定だとexeの隣に書き込もうとして権限や配置によって初期化に失敗する)
        Browser.CreationProperties = new Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(App.AppDataRoot, "webview2")
        };
        Browser.SourceChanged += (_, _) => BrowserUrlBox.Text = Browser.Source?.ToString() ?? string.Empty;
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Ctrl+F: ライブラリタブの検索ボックスにフォーカス
        if (e.Key == System.Windows.Input.Key.F
            && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            _viewModel.ShowLibraryTabCommand.Execute(null);
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 保存処理が完了するまでは、再入(ユーザーが保存待ち中にもう一度×を押した場合)も含めて
        // 必ずCancel=trueを先に設定する。条件分岐の後に置くと、再入した2回目の呼び出しが
        // Cancelを設定しないまま抜けてしまい、保存の途中でウィンドウが閉じてしまう。
        e.Cancel = true;
        if (_shutdownInProgress) return;
        _shutdownInProgress = true;

        // 保存後はthis.Close()を再度呼ぶのではなくApplication.Shutdown()でプロセス全体を終了する
        // (同一WindowでCloseをキャンセルした直後に再度Close()を呼ぶと、WPFの内部状態が「閉じかけ」の
        // まま残り2回目のCloseが失敗することがあるため、Window自身のクローズシーケンスに頼らない経路を使う)。
        await _viewModel.Player.ShutdownAsync();
        _audioPlayer.Dispose();

        Application.Current.Shutdown();
    }

    private void OnLibraryDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnLibraryDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

        var library = _viewModel.Library;
        library.IsBusy = true;
        var registered = 0;
        try
        {
            foreach (var path in paths)
            {
                if (!Directory.Exists(path)) continue;
                if (await library.RegisterFolderAsync(path)) registered++;
            }
        }
        finally
        {
            library.IsBusy = false;
            library.StatusMessage = $"{registered}件を登録しました({paths.Length}件中)";
        }
    }

    private async void OnTrackTreeDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { DataContext: TrackTreeNode { IsTrack: true, Track: { } track } })
        {
            return;
        }

        await _viewModel.PlayTrackAsync(track);
    }

    private async void OnPlayAllClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.PlayWorkFromStartAsync();
    }

    private async void OnAddToPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TrackTreeNode { IsTrack: true, Track: { } track } })
        {
            return;
        }

        await _viewModel.Playlists.LoadAsync();
        var dialog = new AddToPlaylistDialog(_viewModel.Playlists) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedPlaylist is null) return;

        await _viewModel.Playlists.AddTrackToPlaylistAsync(dialog.SelectedPlaylist.Id, track);
    }

    private async void OnCreatePlaylistClick(object sender, RoutedEventArgs e)
    {
        var dialog = new TextInputDialog("新規プレイリスト", "プレイリスト名を入力してください") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await _viewModel.Playlists.CreatePlaylistAsync(dialog.InputText);
    }

    private async void OnPlayPlaylistClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.PlayPlaylistFromStartAsync();
    }

    private async void OnPlaylistTrackDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { DataContext: Track track })
        {
            return;
        }

        await _viewModel.PlayPlaylistTrackAsync(track);
    }

    private async void OnWatchFoldersClick(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        var dialog = new WatchFoldersDialog(app.Database) { Owner = this };
        dialog.ShowDialog();

        if (dialog.ChangesMade)
        {
            await _viewModel.Library.RefreshWatchFoldersAsync();
        }
    }

    private void OnRecentlyPlayedClick(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        var dialog = new RecentlyPlayedDialog(app.Database, _viewModel) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnOpenInExplorerClick(object sender, RoutedEventArgs e)
    {
        var work = _viewModel.Library.WorkDetail.Work;
        if (work is null || !Directory.Exists(work.FolderPath)) return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = work.FolderPath,
            UseShellExecute = true
        });
    }

    private void OnPropertiesClick(object sender, RoutedEventArgs e)
    {
        var work = _viewModel.Library.WorkDetail.Work;
        if (work is null) return;

        var app = (App)Application.Current;
        var dialog = new PropertyDialog(work, app.Database) { Owner = this };
        dialog.ShowDialog();

        if (dialog.Saved)
        {
            // タイトル/タグ変更を検索・絞り込みへ即時反映する
            _viewModel.Library.WorksView.Refresh();
        }
    }

    private async void OnDeleteWorkClick(object sender, RoutedEventArgs e)
    {
        var work = _viewModel.Library.WorkDetail.Work;
        if (work is null) return;

        var answer = MessageBox.Show(this,
            $"「{work.Title}」をライブラリの一覧から削除しますか?\n\n" +
            "・ディスク上のフォルダやファイルは削除されません\n" +
            "・監視フォルダによる自動登録では復活しません\n" +
            "・「＋作品追加」等で手動追加すれば元に戻せます",
            "一覧から削除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await _viewModel.Library.DeleteWorkFromLibraryAsync(work);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_viewModel.Player) { Owner = this };
        dialog.ShowDialog();
    }

    // ---------- 内蔵ブラウザ ----------

    private void OnOpenInBrowserClick(object sender, RoutedEventArgs e)
    {
        var url = _viewModel.GetSelectedWorkDlsiteUrl();
        if (url is null) return;

        _viewModel.ShowBrowserTabCommand.Execute(null);
        BrowserNavigate(url);
    }

    private void OnBrowserBackClick(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.CanGoBack == true) Browser.CoreWebView2.GoBack();
    }

    private void OnBrowserForwardClick(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.CanGoForward == true) Browser.CoreWebView2.GoForward();
    }

    private void OnBrowserReloadClick(object sender, RoutedEventArgs e)
    {
        Browser.CoreWebView2?.Reload();
    }

    private void OnBrowserUrlKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;

        var input = BrowserUrlBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            input = "https://" + input;
        }
        BrowserNavigate(input);
    }

    private async void BrowserNavigate(string url)
    {
        try
        {
            // 初回はWebView2ランタイムの初期化が必要(2回目以降は即時完了する)
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            ((App)Application.Current).LogService.Error($"内蔵ブラウザの初期化/遷移に失敗: {url}", ex);
            MessageBox.Show(this,
                $"内蔵ブラウザを開けませんでした。WebView2ランタイムが必要です。\n{ex.Message}",
                "DLVoiceLibrary", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
