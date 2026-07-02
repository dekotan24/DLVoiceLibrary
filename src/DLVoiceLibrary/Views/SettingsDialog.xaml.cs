using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DLVoiceLibrary.Services;
using DLVoiceLibrary.ViewModels;

namespace DLVoiceLibrary.Views;

/// <summary>アプリ設定ダイアログ。音声出力デバイスの切り替えとWebメディアサーバの設定。</summary>
public partial class SettingsDialog : Window
{
    private readonly PlayerBarViewModel _player;
    private readonly WebServerService _webServer;

    public SettingsDialog(PlayerBarViewModel player)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _player = player;
        _webServer = ((App)Application.Current).WebServer;

        DeviceCombo.ItemsSource = _player.EnumerateOutputDevices();
        DeviceCombo.SelectedValue = _player.AudioDeviceId;
        DeviceCombo.SelectionChanged += OnDeviceSelectionChanged;

        LoadWebSettings();
        UpdateServerStatus();
    }

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceCombo.SelectedValue is string deviceId)
        {
            _player.AudioDeviceId = deviceId;
        }
    }

    // ------------------------------------------------------------------
    // Webメディアサーバ
    // ------------------------------------------------------------------

    private void LoadWebSettings()
    {
        var s = _webServer.Settings;
        PortBox.Text = s.Port.ToString();
        BindAllCheck.IsChecked = s.BindAll;
        UserBox.Text = s.Username;
        PassHint.Text = s.HasPassword ? "(設定済み。変更する場合のみ入力)" : "(未設定 = 認証なし)";
        AutoStartCheck.IsChecked = s.AutoStart;
    }

    /// <summary>UIの値を設定へ反映。パスワード欄は入力があった場合のみ更新する。</summary>
    private bool ApplyWebSettings()
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1024 or > 65535)
        {
            MessageBox.Show("ポートは 1024〜65535 の数値で指定してください。", "設定",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var s = _webServer.Settings;
        s.Port = port;
        s.BindAll = BindAllCheck.IsChecked == true;
        s.Username = string.IsNullOrWhiteSpace(UserBox.Text) ? "admin" : UserBox.Text.Trim();
        s.AutoStart = AutoStartCheck.IsChecked == true;
        if (PassBox.Password.Length > 0)
        {
            s.SetPassword(PassBox.Password);
            PassBox.Clear();
        }
        s.Save();
        PassHint.Text = s.HasPassword ? "(設定済み。変更する場合のみ入力)" : "(未設定 = 認証なし)";
        return true;
    }

    private void OnSaveWebSettingsClick(object sender, RoutedEventArgs e)
    {
        if (ApplyWebSettings() && _webServer.IsRunning)
        {
            MessageBox.Show("設定を保存しました。ポート/アクセス範囲の変更はサーバ再起動後に反映されます。",
                "設定", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        StartStopButton.IsEnabled = false;
        try
        {
            if (_webServer.IsRunning)
            {
                await _webServer.StopAsync();
            }
            else
            {
                if (!ApplyWebSettings()) return;
                await _webServer.StartAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"サーバの起動に失敗しました:\n{ex.Message}\n\nポートが他のアプリで使用中の可能性があります。",
                "Webメディアサーバ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            StartStopButton.IsEnabled = true;
            UpdateServerStatus();
        }
    }

    private void UpdateServerStatus()
    {
        var running = _webServer.IsRunning;
        StartStopButton.Content = running ? "サーバを停止" : "サーバを開始";
        UrlPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        UrlText.Text = _webServer.BaseUrl ?? "";
    }

    private void OnUrlClick(object sender, MouseButtonEventArgs e)
    {
        if (_webServer.BaseUrl is { } url)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private void OnCopyUrlClick(object sender, RoutedEventArgs e)
    {
        if (_webServer.BaseUrl is { } url)
        {
            Clipboard.SetText(url);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
