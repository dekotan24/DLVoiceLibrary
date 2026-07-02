namespace DLVoiceLibrary.Core.Services;

public interface ILogService
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
