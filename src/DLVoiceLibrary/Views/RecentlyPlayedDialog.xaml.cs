using System.Windows;
using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;
using DLVoiceLibrary.ViewModels;

namespace DLVoiceLibrary.Views;

public partial class RecentlyPlayedDialog : Window
{
    private readonly IDatabaseService _database;
    private readonly MainViewModel _mainViewModel;
    private List<Track> _tracks = [];

    public RecentlyPlayedDialog(IDatabaseService database, MainViewModel mainViewModel)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _database = database;
        _mainViewModel = mainViewModel;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _tracks = await _database.GetRecentlyPlayedTracksAsync(50);
        TracksListBox.ItemsSource = _tracks;
    }

    private async void OnTrackDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TracksListBox.SelectedItem is not Track track) return;

        await _mainViewModel.PlayRecentTracksAsync(_tracks, track);
    }
}
