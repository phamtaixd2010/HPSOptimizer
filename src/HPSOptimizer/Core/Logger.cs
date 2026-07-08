using System.IO;
namespace HPSOptimizer.Core;

public enum LogLevel { Info, Success, Warn, Error }

public sealed record LogItem(DateTime Time, LogLevel Level, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss");
    public string LevelText => Level switch
    {
        LogLevel.Success => "OK",
        LogLevel.Warn => "CẢNH BÁO",
        LogLevel.Error => "LỖI",
        _ => "THÔNG TIN"
    };
}

public static class Logger
{
    private static readonly object Gate = new();

    /// <summary>Bắn trên thread gọi Log(); UI phải tự marshal về Dispatcher.</summary>
    public static event Action<LogItem>? Written;

    public static void Log(LogLevel level, string message)
    {
        var item = new LogItem(DateTime.Now, level, message);
        try
        {
            lock (Gate)
                File.AppendAllText(Paths.LogFile,
                    $"{item.Time:yyyy-MM-dd HH:mm:ss}\t{item.Level}\t{message}{Environment.NewLine}");
        }
        catch
        {
            // Ghi log không được thì cũng không làm sập app.
        }
        Written?.Invoke(item);
    }

    public static void Info(string m) => Log(LogLevel.Info, m);
    public static void Success(string m) => Log(LogLevel.Success, m);
    public static void Warn(string m) => Log(LogLevel.Warn, m);
    public static void Error(string m) => Log(LogLevel.Error, m);
    public static void Error(string context, Exception ex) => Log(LogLevel.Error, $"{context}: {ex.Message}");
}
