using System.Windows;
using DLVoiceLibrary.ViewModels;

namespace DLVoiceLibrary.Views;

public partial class SleepTimerDialog : Window
{
    private readonly PlayerBarViewModel _player;

    public SleepTimerDialog(PlayerBarViewModel player)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _player = player;
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (AtTrackEnd.IsChecked == true)
        {
            _player.StartSleepAtTrackEnd();
        }
        else
        {
            var minutes = Minutes15.IsChecked == true ? 15
                : Minutes30.IsChecked == true ? 30
                : Minutes45.IsChecked == true ? 45
                : 60;
            _player.StartSleepTimer(TimeSpan.FromMinutes(minutes));
        }

        DialogResult = true;
    }

    private void OnCancelTimerClick(object sender, RoutedEventArgs e)
    {
        _player.CancelSleepTimerCommand.Execute(null);
        DialogResult = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
