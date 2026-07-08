using System.Management;
using System.ServiceProcess;
using HPSOptimizer.Core;

namespace HPSOptimizer.Services;

public sealed class ServiceItem : ObservableObject
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Advice { get; init; }
    /// <summary>Chế độ khởi động khuyến nghị: Automatic / Manual / Disabled.</summary>
    public required string Recommended { get; init; }
    public required string Impact { get; init; }

    private string _startMode = "?";
    public string StartMode { get => _startMode; set => Set(ref _startMode, value); }

    private string _status = "?";
    public string Status { get => _status; set => Set(ref _status, value); }

    private bool _selected;
    public bool Selected { get => _selected; set => Set(ref _selected, value); }

    public bool IsOptimized => string.Equals(StartMode, Recommended, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Danh sách service được cắt gọn. Chỉ gồm các service mà Microsoft và cộng đồng
/// đều xác nhận là tắt/chuyển Manual được mà không hỏng Windows Update hay bảo mật.
/// </summary>
public static class ServiceTweakService
{
    public static List<ServiceItem> Catalog() => new()
    {
        new() { Name = "DiagTrack", DisplayName = "Connected User Experiences and Telemetry",
                Recommended = "Disabled", Impact = "Cao",
                Advice = "Gửi dữ liệu chẩn đoán về Microsoft. Tắt an toàn, không ảnh hưởng Windows Update." },
        new() { Name = "dmwappushservice", DisplayName = "WAP Push Message Routing",
                Recommended = "Disabled", Impact = "Thấp",
                Advice = "Kênh phụ của telemetry. Chỉ cần nếu máy được quản lý bằng MDM doanh nghiệp." },
        new() { Name = "SysMain", DisplayName = "SysMain (Superfetch)",
                Recommended = "Disabled", Impact = "Cao",
                Advice = "Nạp trước app vào RAM. Có ích trên HDD, nhưng ăn RAM và ghi đĩa liên tục — nên tắt nếu máy dùng SSD hoặc dưới 8GB RAM." },
        new() { Name = "WSearch", DisplayName = "Windows Search",
                Recommended = "Manual", Impact = "Cao",
                Advice = "Đánh chỉ mục tìm kiếm. Chuyển Manual để bớt ghi đĩa nền; đổi lại tìm file trong Start sẽ chậm." },
        new() { Name = "Spooler", DisplayName = "Print Spooler",
                Recommended = "Manual", Impact = "Thấp",
                Advice = "Chỉ tắt nếu máy không in. Cũng là bề mặt tấn công của lỗ hổng PrintNightmare." },
        new() { Name = "Fax", DisplayName = "Fax",
                Recommended = "Disabled", Impact = "Thấp", Advice = "Gần như không ai còn dùng." },
        new() { Name = "RetailDemo", DisplayName = "Retail Demo Service",
                Recommended = "Disabled", Impact = "Thấp", Advice = "Chỉ dùng cho máy trưng bày ở cửa hàng." },
        new() { Name = "MapsBroker", DisplayName = "Downloaded Maps Manager",
                Recommended = "Disabled", Impact = "Thấp", Advice = "Chỉ cần nếu bạn dùng app Maps ngoại tuyến." },
        new() { Name = "RemoteRegistry", DisplayName = "Remote Registry",
                Recommended = "Disabled", Impact = "Thấp", Advice = "Cho phép sửa registry từ xa. Nên tắt vì lý do bảo mật." },
        new() { Name = "lfsvc", DisplayName = "Geolocation Service",
                Recommended = "Manual", Impact = "Trung bình", Advice = "Định vị. Chuyển Manual nếu không dùng app bản đồ/thời tiết." },
        new() { Name = "XblAuthManager", DisplayName = "Xbox Live Auth Manager",
                Recommended = "Manual", Impact = "Thấp", Advice = "Chỉ cần nếu chơi game qua Xbox / Game Pass." },
        new() { Name = "XblGameSave", DisplayName = "Xbox Live Game Save",
                Recommended = "Manual", Impact = "Thấp", Advice = "Đồng bộ save game Xbox." },
        new() { Name = "XboxGipSvc", DisplayName = "Xbox Accessory Management",
                Recommended = "Manual", Impact = "Thấp", Advice = "Chỉ cần nếu cắm tay cầm Xbox." },
        new() { Name = "XboxNetApiSvc", DisplayName = "Xbox Live Networking",
                Recommended = "Manual", Impact = "Thấp", Advice = "Mạng cho game Xbox." },
        new() { Name = "WMPNetworkSvc", DisplayName = "Windows Media Player Network Sharing",
                Recommended = "Disabled", Impact = "Thấp", Advice = "Chia sẻ thư viện media qua mạng LAN." },
        new() { Name = "PcaSvc", DisplayName = "Program Compatibility Assistant",
                Recommended = "Manual", Impact = "Trung bình", Advice = "Theo dõi app cũ để gợi ý chế độ tương thích." },
    };

    /// <summary>Đọc StartMode + Status thực tế. Service không tồn tại sẽ bị loại khỏi danh sách.</summary>
    public static List<ServiceItem> LoadInstalled()
    {
        var result = new List<ServiceItem>();
        foreach (var item in Catalog())
        {
            try
            {
                using var sc = new ServiceController(item.Name);
                item.Status = TranslateStatus(sc.Status);
                item.StartMode = NormalizeStartMode(QueryStartMode(item.Name));
                item.OnPropertyChanged(nameof(ServiceItem.IsOptimized));
                result.Add(item);
            }
            catch (InvalidOperationException)
            {
                // Service không có trên bản Windows này (ví dụ Fax trên Windows 11 mới) → bỏ qua.
            }
            catch (Exception ex)
            {
                Logger.Warn($"Không đọc được service {item.Name}: {ex.Message}");
            }
        }
        return result;
    }

    private static string QueryStartMode(string name)
    {
        var escaped = name.Replace("'", "''");
        using var searcher = new ManagementObjectSearcher(
            $"SELECT StartMode FROM Win32_Service WHERE Name = '{escaped}'");
        foreach (ManagementObject mo in searcher.Get())
            using (mo)
                return mo["StartMode"]?.ToString() ?? "Unknown";
        return "Unknown";
    }

    /// <summary>WMI trả "Auto" nhưng ChangeStartMode lại nhận "Automatic". Chuẩn hoá về dạng ChangeStartMode.</summary>
    private static string NormalizeStartMode(string raw) => raw switch
    {
        "Auto" => "Automatic",
        _ => raw
    };

    public static void SetStartMode(string serviceName, string startMode)
    {
        var escaped = serviceName.Replace("'", "''");
        using var mo = new ManagementObject($"Win32_Service.Name='{escaped}'");
        var result = mo.InvokeMethod("ChangeStartMode", new object[] { NormalizeStartMode(startMode) });
        var code = Convert.ToUInt32(result);
        if (code != 0)
            throw new InvalidOperationException($"ChangeStartMode trả mã lỗi {code} cho service '{serviceName}'.");
    }

    /// <summary>Áp dụng khuyến nghị: ghi nhật ký hoàn tác → đổi StartMode → dừng service nếu Disabled.</summary>
    public static void Apply(ServiceItem item)
    {
        var old = item.StartMode;
        if (string.Equals(old, item.Recommended, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Info($"{item.DisplayName} đã ở chế độ {item.Recommended}.");
            return;
        }

        UndoJournal.Add(new UndoEntry
        {
            Kind = UndoKind.ServiceStartMode,
            Title = $"Service: {item.DisplayName}",
            Target = item.Name,
            OldValue = old,
            NewValue = item.Recommended
        });

        SetStartMode(item.Name, item.Recommended);

        if (item.Recommended.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            TryStop(item.Name);

        item.StartMode = item.Recommended;
        RefreshStatus(item);
        Logger.Success($"{item.DisplayName}: {old} → {item.Recommended}");
    }

    private static void TryStop(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending) return;
            if (!sc.CanStop) { Logger.Warn($"Service {name} không cho phép dừng, sẽ tắt sau khi khởi động lại."); return; }
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            Logger.Warn($"Không dừng được service {name}: {ex.Message}. Nó sẽ không chạy sau lần khởi động lại tới.");
        }
    }

    public static void RefreshStatus(ServiceItem item)
    {
        try
        {
            using var sc = new ServiceController(item.Name);
            item.Status = TranslateStatus(sc.Status);
            item.StartMode = NormalizeStartMode(QueryStartMode(item.Name));
            item.OnPropertyChanged(nameof(ServiceItem.IsOptimized));
        }
        catch { /* service đã biến mất */ }
    }

    private static string TranslateStatus(ServiceControllerStatus s) => s switch
    {
        ServiceControllerStatus.Running => "Đang chạy",
        ServiceControllerStatus.Stopped => "Đã dừng",
        ServiceControllerStatus.Paused => "Tạm dừng",
        ServiceControllerStatus.StartPending => "Đang khởi động",
        ServiceControllerStatus.StopPending => "Đang dừng",
        _ => s.ToString()
    };
}
