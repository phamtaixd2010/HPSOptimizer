using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPSOptimizer.Core;

namespace HPSOptimizer.Services;

public enum UndoKind
{
    RegistryValue,   // Target = "HIVE|SubKey|ValueName", OldValue/NewValue = dữ liệu, Extra = RegistryValueKind
    ServiceStartMode,// Target = tên service
    StartupRegistry, // Target = "HIVE|SubKey|ValueName"
    StartupFile,     // Target = đường dẫn .lnk hiện tại (đã bị đổi tên)
    PowerPlan,       // OldValue = GUID cũ
    Command          // Chỉ ghi nhận, hoàn tác bằng lệnh trong Extra
}

public sealed class UndoEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime Time { get; set; } = DateTime.Now;
    public UndoKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Target { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    /// <summary>Thông tin phụ: kiểu registry, lệnh hoàn tác…</summary>
    public string? Extra { get; set; }
    /// <summary>true = OldValue không tồn tại (giá trị mới do ta tạo ra → hoàn tác = xoá).</summary>
    public bool OldMissing { get; set; }
    public bool Reverted { get; set; }

    [JsonIgnore] public string TimeText => Time.ToString("dd/MM HH:mm");
    [JsonIgnore] public string StatusText => Reverted ? "Đã hoàn tác" : "Đang áp dụng";
}

/// <summary>Nhật ký hoàn tác, lưu JSON ở %ProgramData%\HPSOptimizer\undo.json.</summary>
public static class UndoJournal
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static List<UndoEntry> Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(Paths.UndoFile)) return new List<UndoEntry>();
                var json = File.ReadAllText(Paths.UndoFile);
                return JsonSerializer.Deserialize<List<UndoEntry>>(json, JsonOpts) ?? new List<UndoEntry>();
            }
            catch (Exception ex)
            {
                Logger.Error("Đọc nhật ký hoàn tác", ex);
                return new List<UndoEntry>();
            }
        }
    }

    private static void Save(List<UndoEntry> entries)
    {
        lock (Gate)
        {
            try { File.WriteAllText(Paths.UndoFile, JsonSerializer.Serialize(entries, JsonOpts)); }
            catch (Exception ex) { Logger.Error("Ghi nhật ký hoàn tác", ex); }
        }
    }

    public static void Add(UndoEntry entry)
    {
        var all = Load();
        all.Insert(0, entry);
        // Giữ 500 mục gần nhất.
        if (all.Count > 500) all.RemoveRange(500, all.Count - 500);
        Save(all);
    }

    public static void MarkReverted(string id)
    {
        var all = Load();
        var e = all.FirstOrDefault(x => x.Id == id);
        if (e is null) return;
        e.Reverted = true;
        Save(all);
    }

    public static void Clear()
    {
        Save(new List<UndoEntry>());
        Logger.Info("Đã xoá nhật ký hoàn tác.");
    }
}
