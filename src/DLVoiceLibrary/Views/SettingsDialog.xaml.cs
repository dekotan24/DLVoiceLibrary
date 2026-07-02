using System.Windows;
using System.Windows.Controls;
using DLVoiceLibrary.ViewModels;

namespace DLVoiceLibrary.Views;

/// <summary>アプリ設定ダイアログ。現状は音声出力デバイスの切り替えのみ(選択は即時適用+永続化)。</summary>
public partial class SettingsDialog : Window
{
    private readonly PlayerBarViewModel _player;

    public SettingsDialog(PlayerBarViewModel player)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _player = player;

        DeviceCombo.ItemsSource = _player.EnumerateOutputDevices();
        DeviceCombo.SelectedValue = _player.AudioDeviceId;
        DeviceCombo.SelectionChanged += OnDeviceSelectionChanged;
    }

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceCombo.SelectedValue is string deviceId)
        {
            _player.AudioDeviceId = deviceId;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
