using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using DLVoiceLibrary.Core.Services;

namespace DLVoiceLibrary;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static string AppDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DLVoiceLibrary");

    public static string ThumbnailsDirectory => Path.Combine(AppDataRoot, "thumbnails");

    public ILogService LogService { get; private set; } = null!;
    public IDatabaseService Database { get; private set; } = null!;

    /// <summary>DLsiteスクレイピング・サムネイルDL共用のHttpClient。UA偽装+年齢確認Cookieを設定済み。</summary>
    public HttpClient DlsiteHttpClient { get; } = CreateDlsiteHttpClient();

    private static HttpClient CreateDlsiteHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Cookie", "adultchecked=1");
        return client;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(ThumbnailsDirectory);

        LogService = new FileLogService(Path.Combine(AppDataRoot, "app.log"));
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        Database = new SqliteDatabaseService(Path.Combine(AppDataRoot, "library.db"));

        try
        {
            await Database.InitializeAsync();
            LogService.Info("Database initialized.");
        }
        catch (Exception ex)
        {
            LogService.Error("Database initialization failed.", ex);
            MessageBox.Show($"データベースの初期化に失敗しました:\n{ex.Message}", "DLVoiceLibrary",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.Error("Unhandled UI exception.", e.Exception);
        WriteCrashLog(e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogService.Error("Unhandled domain exception.", ex);
            WriteCrashLog(ex);
        }
    }

    private void WriteCrashLog(Exception ex)
    {
        try
        {
            var path = Path.Combine(AppDataRoot, "crash.log");
            File.AppendAllText(path, $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch
        {
            // クラッシュログの書き込み失敗はこれ以上どうにもできない
        }
    }
}
