using System.IO;
using Microsoft.Win32;
using HPSOptimizer.Core;

namespace HPSOptimizer.Services;

public enum StartupSource { RegistryRun, StartupFolder }

public sealed class StartupItem : ObservableObject
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required StartupSource Source { get; init; }
    public required string Location { get; init; }

    // Dành cho registry
    public string? Hive { get; init; }
    public string? SubKey { get; init; }
    public RegistryValueKind Kind { get; init; } = RegistryValueKind.String;

    // Dành cho file .lnk
    public string? FilePath { get; init; }

    private bool _selected;
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
}

/// <summary>
/// Quản lý app khởi động cùng Windows.
/// Cách tắt: xoá value registry (đã lưu vào nhật ký hoàn tác) hoặc đổi đuôi .lnk → .lnk.hpsdisabled.
/// Cả hai đều hoàn tác được 100%. Ta cố tình KHÔNG đụng vào blob StartupApproved của Task Manager
/// vì định dạng đó không có tài liệu chính thức.
/// </summary>
public static class StartupService
{
    private const string DisabledSuffix = ".hpsdisabled";

    private static readonly (string Hive, string SubKey)[] RunKeys =
    {
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run"),
        ("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run"),
        ("HKLM", @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run"),
    };

    public static List<StartupItem> Load()
    {
        var items = new List<StartupItem>();

        foreach (var (hive, subKey) in RunKeys)
        {
            try
            {
                using var root = RegistryHelper.OpenBase(hive);
                using var key = root.OpenSubKey(subKey, writable: false);
                if (key is null) continue;

                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName);
                    if (value is null) continue;
                    items.Add(new StartupItem
                    {
                        Name = valueName,
                        Command = RegistryHelper.Serialize(value),
                        Source = StartupSource.RegistryRun,
                        Location = $"{hive}\\{subKey}",
                        Hive = hive,
                        SubKey = subKey,
                        Kind = key.GetValueKind(valueName)
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Đọc {hive}\\{subKey} thất bại: {ex.Message}");
            }
        }

        foreach (var folder in StartupFolders())
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    var ext = Path.GetExtension(file);
                    if (!ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".hpsdisabled", StringComparison.OrdinalIgnoreCase)) continue;

                    items.Add(new StartupItem
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        Source = StartupSource.StartupFolder,
                        Location = folder,
                        FilePath = file
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Đọc thư mục Startup {folder} thất bại: {ex.Message}");
            }
        }

        return items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static IEnumerable<string> StartupFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
    }

    public static void Disable(StartupItem item)
    {
        if (item.Source == StartupSource.RegistryRun)
        {
            UndoJournal.Add(new UndoEntry
            {
                Kind = UndoKind.StartupRegistry,
                Title = $"Startup: {item.Name}",
                Target = $"{item.Hive}|{item.SubKey}|{item.Name}",
                OldValue = item.Command,
                NewValue = null,
                Extra = item.Kind.ToString(),
                OldMissing = false
            });
            RegistryHelper.Delete(item.Hive!, item.SubKey!, item.Name);
        }
        else
        {
            var path = item.FilePath!;
            if (path.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"{item.Name} đã bị tắt sẵn.");
                return;
            }
            var disabled = path + DisabledSuffix;
            File.Move(path, disabled, overwrite: true);

            UndoJournal.Add(new UndoEntry
            {
                Kind = UndoKind.StartupFile,
                Title = $"Startup: {item.Name}",
                Target = disabled,   // vị trí hiện tại
                OldValue = path,     // tên gốc cần khôi phục
                NewValue = disabled
            });
        }

        Logger.Success($"Đã tắt khởi động cùng Windows: {item.Name}");
    }
}
