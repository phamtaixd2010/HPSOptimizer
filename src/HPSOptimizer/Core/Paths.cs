using System.IO;
namespace HPSOptimizer.Core;

/// <summary>Đường dẫn dữ liệu của app (nằm ở %ProgramData%\HPSOptimizer).</summary>
public static class Paths
{
    public static string DataDir { get; }

    static Paths()
    {
        DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HPSOptimizer");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(Path.Combine(DataDir, "logs"));
    }

    public static string LogDir => Path.Combine(DataDir, "logs");
    public static string LogFile => Path.Combine(LogDir, $"{DateTime.Now:yyyy-MM-dd}.log");
    public static string UndoFile => Path.Combine(DataDir, "undo.json");
}
