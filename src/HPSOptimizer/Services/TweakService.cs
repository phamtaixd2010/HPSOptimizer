using Microsoft.Win32;
using HPSOptimizer.Core;

namespace HPSOptimizer.Services;

public enum RiskLevel { Safe, Medium }

public sealed class Tweak : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public RiskLevel Risk { get; init; } = RiskLevel.Safe;
    /// <summary>Cần đăng xuất / khởi động lại mới có hiệu lực.</summary>
    public bool NeedsRestart { get; init; }

    // --- Dạng registry ---
    public string? Hive { get; init; }
    public string? SubKey { get; init; }
    public string? ValueName { get; init; }
    public object? Optimized { get; init; }
    public RegistryValueKind Kind { get; init; } = RegistryValueKind.DWord;

    // --- Dạng lệnh ---
    public Func<Task>? ApplyCommand { get; init; }
    public Func<Task>? RevertCommand { get; init; }
    public Func<Task<bool>>? IsAppliedCheck { get; init; }

    public bool IsRegistry => Hive is not null;
    public string RiskText => Risk == RiskLevel.Safe ? "An toàn" : "Cân nhắc";

    private bool _selected;
    public bool Selected { get => _selected; set => Set(ref _selected, value); }

    private string _state = "Đang kiểm tra…";
    public string State { get => _state; set => Set(ref _state, value); }

    private bool _isApplied;
    public bool IsApplied { get => _isApplied; set { if (Set(ref _isApplied, value)) State = value ? "Đã tối ưu" : "Chưa tối ưu"; } }
}

public static class TweakService
{
    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public static List<Tweak> BuildAll() => new()
    {
        new Tweak
        {
            Id = "visualfx",
            Name = "Tắt hiệu ứng trực quan (Best Performance)",
            Description = "Bỏ đổ bóng, mờ nền, animation cửa sổ. Đây là tác động lớn nhất tới cảm giác mượt trên máy yếu.",
            Hive = "HKCU", SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            ValueName = "VisualFXSetting", Optimized = 2, Kind = RegistryValueKind.DWord,
            NeedsRestart = true
        },
        new Tweak
        {
            Id = "minanimate",
            Name = "Tắt animation phóng to / thu nhỏ cửa sổ",
            Description = "Cửa sổ xuất hiện tức thì thay vì trượt.",
            Hive = "HKCU", SubKey = @"Control Panel\Desktop\WindowMetrics",
            ValueName = "MinAnimate", Optimized = "0", Kind = RegistryValueKind.String,
            NeedsRestart = true
        },
        new Tweak
        {
            Id = "menudelay",
            Name = "Bỏ độ trễ khi mở menu",
            Description = "MenuShowDelay từ 400ms xuống 0ms. Menu bật ra ngay khi rê chuột.",
            Hive = "HKCU", SubKey = @"Control Panel\Desktop",
            ValueName = "MenuShowDelay", Optimized = "0", Kind = RegistryValueKind.String,
            NeedsRestart = true
        },
        new Tweak
        {
            Id = "transparency",
            Name = "Tắt hiệu ứng trong suốt (Acrylic / Mica)",
            Description = "Taskbar và Start menu vẽ nền đặc. Giảm tải GPU tích hợp đáng kể.",
            Hive = "HKCU", SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            ValueName = "EnableTransparency", Optimized = 0, Kind = RegistryValueKind.DWord
        },
        new Tweak
        {
            Id = "startupdelay",
            Name = "Bỏ độ trễ 10 giây khi khởi động app startup",
            Description = "Windows cố tình hoãn app startup ~10s sau khi vào desktop. Tắt đi để máy sẵn sàng sớm hơn.",
            Hive = "HKCU", SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize",
            ValueName = "StartupDelayInMSec", Optimized = 0, Kind = RegistryValueKind.DWord
        },
        new Tweak
        {
            Id = "gamedvr",
            Name = "Tắt Xbox Game Bar / Game DVR",
            Description = "Dừng ghi hình nền của Game Bar. Trả lại vài phần trăm CPU và RAM.",
            Hive = "HKCU", SubKey = @"System\GameConfigStore",
            ValueName = "GameDVR_Enabled", Optimized = 0, Kind = RegistryValueKind.DWord
        },
        new Tweak
        {
            Id = "appcapture",
            Name = "Tắt App Capture nền",
            Description = "Đi kèm với Game DVR ở trên.",
            Hive = "HKCU", SubKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
            ValueName = "AppCaptureEnabled", Optimized = 0, Kind = RegistryValueKind.DWord
        },
        new Tweak
        {
            Id = "contentdelivery",
            Name = "Tắt gợi ý & app tự cài trong Start menu",
            Description = "Windows tự tải app quảng cáo (Candy Crush, TikTok…) về máy. Tắt hẳn.",
            Hive = "HKCU", SubKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SilentInstalledAppsEnabled", Optimized = 0, Kind = RegistryValueKind.DWord
        },
        new Tweak
        {
            Id = "telemetry",
            Name = "Giảm thu thập dữ liệu (Telemetry) về mức tối thiểu",
            Description = "Đặt AllowTelemetry = 0. Trên Windows 11 Home/Pro mức 0 bị nâng lên 1 (Basic), " +
                          "nhưng vẫn cắt được phần lớn dữ liệu gửi đi. Không ảnh hưởng Windows Update.",
            Risk = RiskLevel.Medium,
            Hive = "HKLM", SubKey = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
            ValueName = "AllowTelemetry", Optimized = 0, Kind = RegistryValueKind.DWord,
            NeedsRestart = true
        },
        new Tweak
        {
            Id = "poweplan",
            Name = "Chuyển Power Plan sang High Performance",
            Description = "CPU không hạ xung khi tải nhẹ. Máy bàn nên bật; laptop sẽ tốn pin hơn.",
            Risk = RiskLevel.Medium,
            IsAppliedCheck = IsHighPerfActiveAsync,
            ApplyCommand = ApplyHighPerfAsync,
            RevertCommand = RevertPowerPlanAsync
        },
        new Tweak
        {
            Id = "hibernate",
            Name = "Tắt Hibernate (giải phóng hiberfil.sys)",
            Description = "Xoá file hiberfil.sys bằng ~40% dung lượng RAM trên ổ C:. " +
                          "Đổi lại mất chế độ ngủ đông và Fast Startup.",
            Risk = RiskLevel.Medium,
            IsAppliedCheck = IsHibernateOffAsync,
            ApplyCommand = () => RunPowerCfgAsync("/h off"),
            RevertCommand = () => RunPowerCfgAsync("/h on")
        },
    };

    // ---------------------------------------------------------------- trạng thái

    public static async Task RefreshStateAsync(Tweak t)
    {
        try
        {
            if (t.IsRegistry)
            {
                var cur = RegistryHelper.Read(t.Hive!, t.SubKey!, t.ValueName!);
                t.IsApplied = cur is not null && RegistryHelper.Serialize(cur) == RegistryHelper.Serialize(t.Optimized!);
            }
            else if (t.IsAppliedCheck is not null)
            {
                t.IsApplied = await t.IsAppliedCheck().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            t.State = "Không đọc được";
            Logger.Warn($"Kiểm tra tinh chỉnh '{t.Name}' thất bại: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- áp dụng

    public static async Task ApplyAsync(Tweak t)
    {
        if (t.IsRegistry)
        {
            var old = RegistryHelper.Read(t.Hive!, t.SubKey!, t.ValueName!);
            UndoJournal.Add(new UndoEntry
            {
                Kind = UndoKind.RegistryValue,
                Title = t.Name,
                Target = $"{t.Hive}|{t.SubKey}|{t.ValueName}",
                OldValue = old is null ? null : RegistryHelper.Serialize(old),
                NewValue = RegistryHelper.Serialize(t.Optimized!),
                Extra = t.Kind.ToString(),
                OldMissing = old is null
            });
            RegistryHelper.Write(t.Hive!, t.SubKey!, t.ValueName!, t.Optimized!, t.Kind);
        }
        else if (t.ApplyCommand is not null)
        {
            await t.ApplyCommand().ConfigureAwait(false);
        }

        Logger.Success($"Đã áp dụng: {t.Name}" + (t.NeedsRestart ? " (cần đăng xuất/khởi động lại)" : ""));
        await RefreshStateAsync(t).ConfigureAwait(false);
    }

    public static async Task RevertAsync(Tweak t)
    {
        if (t.IsRegistry)
        {
            // Tìm mục hoàn tác gần nhất chưa revert của tweak này.
            var entry = UndoJournal.Load()
                .FirstOrDefault(e => !e.Reverted && e.Kind == UndoKind.RegistryValue &&
                                     e.Target == $"{t.Hive}|{t.SubKey}|{t.ValueName}");
            if (entry is null)
            {
                Logger.Warn($"Không có bản ghi hoàn tác cho '{t.Name}'.");
                return;
            }
            RevertEntry(entry);
        }
        else if (t.RevertCommand is not null)
        {
            await t.RevertCommand().ConfigureAwait(false);
            Logger.Success($"Đã hoàn tác: {t.Name}");
        }

        await RefreshStateAsync(t).ConfigureAwait(false);
    }

    /// <summary>Hoàn tác một mục trong nhật ký. Dùng chung cho tab Nhật ký.</summary>
    public static void RevertEntry(UndoEntry entry)
    {
        var parts = entry.Target.Split('|');
        switch (entry.Kind)
        {
            case UndoKind.RegistryValue:
            case UndoKind.StartupRegistry:
                if (parts.Length != 3) throw new InvalidOperationException("Bản ghi hoàn tác hỏng.");
                if (entry.OldMissing || entry.OldValue is null)
                {
                    RegistryHelper.Delete(parts[0], parts[1], parts[2]);
                }
                else
                {
                    var kind = Enum.TryParse<RegistryValueKind>(entry.Extra, out var k) ? k : RegistryValueKind.String;
                    RegistryHelper.Write(parts[0], parts[1], parts[2], RegistryHelper.Coerce(entry.OldValue, kind), kind);
                }
                break;

            case UndoKind.ServiceStartMode:
                if (entry.OldValue is not null)
                    ServiceTweakService.SetStartMode(entry.Target, entry.OldValue);
                break;

            case UndoKind.StartupFile:
                // entry.Target = file đã bị đổi tên; entry.OldValue = tên gốc.
                if (entry.OldValue is not null && File.Exists(entry.Target))
                    File.Move(entry.Target, entry.OldValue, overwrite: false);
                break;

            case UndoKind.PowerPlan:
                if (entry.OldValue is not null)
                    RunPowerCfgAsync($"/setactive {entry.OldValue}").GetAwaiter().GetResult();
                break;

            case UndoKind.Command:
                if (!string.IsNullOrWhiteSpace(entry.Extra))
                    PowerShellRunner.RunAsync(entry.Extra).GetAwaiter().GetResult();
                break;
        }

        UndoJournal.MarkReverted(entry.Id);
        Logger.Success($"Đã hoàn tác: {entry.Title}");
    }

    // ---------------------------------------------------------------- powercfg

    private static Task<PsResult> RunPowerCfgAsync(string args)
        => PowerShellRunner.RunAsync($"powercfg.exe {args}\r\nexit $LASTEXITCODE");

    private static async Task<string> GetActiveSchemeGuidAsync()
    {
        var res = await PowerShellRunner.RunAsync(
            "(powercfg.exe /getactivescheme) -replace '^.*GUID: ([0-9a-f-]+).*$','$1'").ConfigureAwait(false);
        return res.StdOut.Trim();
    }

    private static async Task<bool> IsHighPerfActiveAsync()
        => string.Equals(await GetActiveSchemeGuidAsync().ConfigureAwait(false), HighPerfGuid, StringComparison.OrdinalIgnoreCase);

    private static async Task ApplyHighPerfAsync()
    {
        var current = await GetActiveSchemeGuidAsync().ConfigureAwait(false);
        UndoJournal.Add(new UndoEntry
        {
            Kind = UndoKind.PowerPlan,
            Title = "Power Plan → High Performance",
            Target = "powercfg",
            OldValue = current,
            NewValue = HighPerfGuid
        });

        var res = await RunPowerCfgAsync($"/setactive {HighPerfGuid}").ConfigureAwait(false);
        if (!res.Ok)
        {
            // Một số máy OEM ẩn scheme High Performance → nhân bản rồi kích hoạt.
            Logger.Info("High Performance bị ẩn, đang nhân bản scheme…");
            await RunPowerCfgAsync($"/duplicatescheme {HighPerfGuid}").ConfigureAwait(false);
            await RunPowerCfgAsync($"/setactive {HighPerfGuid}").ConfigureAwait(false);
        }
    }

    private static async Task RevertPowerPlanAsync()
    {
        var entry = UndoJournal.Load().FirstOrDefault(e => !e.Reverted && e.Kind == UndoKind.PowerPlan);
        var target = entry?.OldValue ?? "381b4222-f694-41f0-9685-ff5bb260df2e"; // Balanced
        await RunPowerCfgAsync($"/setactive {target}").ConfigureAwait(false);
        if (entry is not null) UndoJournal.MarkReverted(entry.Id);
    }

    private static Task<bool> IsHibernateOffAsync()
    {
        var v = RegistryHelper.Read("HKLM", @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
        return Task.FromResult(v is int i && i == 0);
    }
}
