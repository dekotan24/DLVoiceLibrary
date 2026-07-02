using System.Windows;
using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.ViewModels;

namespace DLVoiceLibrary.Views;

public partial class AddToPlaylistDialog : Window
{
    private readonly PlaylistsViewModel _playlistsViewModel;

    public Playlist? SelectedPlaylist { get; private set; }

    public AddToPlaylistDialog(PlaylistsViewModel playlistsViewModel)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _playlistsViewModel = playlistsViewModel;
        PlaylistListBox.ItemsSource = _playlistsViewModel.Playlists;
        if (_playlistsViewModel.Playlists.Count > 0)
        {
            PlaylistListBox.SelectedIndex = 0;
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is not Playlist playlist)
        {
            MessageBox.Show(this, "プレイリストを選択してください。", "DLVoiceLibrary", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedPlaylist = playlist;
        DialogResult = true;
    }

    private void OnPlaylistDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is not Playlist playlist) return;
        SelectedPlaylist = playlist;
        DialogResult = true;
    }

    private async void OnCreateNewClick(object sender, RoutedEventArgs e)
    {
        var dialog = new TextInputDialog("新規プレイリスト", "プレイリスト名を入力してください") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var created = await _playlistsViewModel.CreatePlaylistAsync(dialog.InputText);
        SelectedPlaylist = created;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
