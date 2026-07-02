namespace DLVoiceLibrary.Core.Services;

public sealed class FileLogService : ILogService
{
    private readonly string _logFilePath;
    private readonly object _lock = new();
    private const long MaxLogSizeBytes = 1024 * 1024;

    public FileLogService(string logFilePath)
    {
        _logFilePath = logFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message} :: {exception}";
        Write("ERROR", text);
    }

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            RotateIfNeeded();
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            try
            {
                File.AppendAllLines(_logFilePath, [line]);
            }
            catch
            {
                // ロギング自体の失敗でアプリを落とさない
            }
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_logFilePath)) return;
        var info = new FileInfo(_logFilePath);
        if (info.Length < MaxLogSizeBytes) return;

        var oldPath = _logFilePath + ".old";
        try
        {
            File.Delete(oldPath);
            File.Move(_logFilePath, oldPath);
        }
        catch
        {
            // ローテーション失敗は無視して書き込み継続
        }
    }
}
