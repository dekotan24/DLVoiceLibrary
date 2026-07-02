using System.Windows;
using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;

namespace DLVoiceLibrary.Views;

public partial class WatchFoldersDialog : Window
{
    private readonly IDatabaseService _database;
    private List<WatchFolder> _folders = [];

    /// <summary>ダイアログ内で追加/削除が行われたかどうか。呼び出し側がリコンサイル要否を判断するのに使う。</summary>
    public bool ChangesMade { get; private set; }

    public WatchFoldersDialog(IDatabaseService database)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _database = database;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _folders = (await _database.GetWatchFoldersAsync()).ToList();
        FoldersListBox.ItemsSource = null;
        FoldersListBox.ItemsSource = _folders.Select(f => f.FolderPath).ToList();
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "自動検出したいフォルダを選択してください(この直下にある作品フォルダが対象になります)"
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        await _database.AddWatchFolderAsync(dialog.SelectedPath);
        ChangesMade = true;
        await ReloadAsync();
    }

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (FoldersListBox.SelectedItem is not string selectedPath) return;

        var target = _folders.FirstOrDefault(f => f.FolderPath == selectedPath);
        if (target is null) return;

        await _database.RemoveWatchFolderAsync(target.Id);
        ChangesMade = true;
        await ReloadAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
