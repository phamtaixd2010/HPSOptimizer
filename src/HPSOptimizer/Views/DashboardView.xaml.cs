using System.Management;
using System.Windows;
using System.Windows.Controls;
using HPSOptimizer.Core;
using HPSOptimizer.Services;

namespace HPSOptimizer.Views;

public partial class DashboardView : UserControl
{
    /// <summary>Các mục dọn rác được coi là an toàn tuyệt đối cho nút "tối ưu nhanh".</summary>
    private static readonly string[] SafeCleanIds = { "usertemp", "wintemp", "wucache", "deliveryopt" };

    /// <summary>Tinh chỉnh chỉ ở mức RiskLevel.Safe mới được áp dụng tự động.</summary>
    public DashboardView()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadSpecs();
    }

    private void LoadSpecs()
    {
        var specs = new List<KeyValuePair<string, string>>
        {
            new("Hệ điều hành", $"{QueryWmi("Win32_OperatingSystem", "Caption")} (build {Environment.OSVersion.Version.Build})"),
            new("Bộ xử lý", $"{QueryWmi("Win32_Processor", "Name")} — {Environment.ProcessorCount} luồng"),
            new("Bộ nhớ RAM", DescribeRam()),
            new("Ổ hệ thống", DescribeSystemDrive()),
            new("Quyền chạy", App.IsAdmin ? "Administrator" : "Người dùng thường (thiếu quyền)"),
            new("Thư mục nhật ký", Paths.DataDir),
        };
        SpecList.ItemsSource = specs;
    }

    private static string QueryWmi(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
            foreach (ManagementObject mo in searcher.Get())
                using (mo)
                    return mo[property]?.ToString()?.Trim() ?? "—";
        }
        catch (Exception ex) { Logger.Warn($"WMI {wmiClass}.{property}: {ex.Message}"); }
        return "—";
    }

    private static string DescribeRam()
    {
        try
        {
            var total = Convert.ToInt64(QueryWmi("Win32_ComputerSystem", "TotalPhysicalMemory"));
            var text = Fmt.Bytes(total);
            if (total < 4L * 1024 * 1024 * 1024) return $"{text} — rất thấp, ưu tiên tắt SysMain và hiệu ứng trực quan";
            if (total < 8L * 1024 * 1024 * 1024) return $"{text} — thấp, nên tắt hiệu ứng trực quan";
            return text;
        }
        catch { return "—"; }
    }

    private static string DescribeSystemDrive()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var di = new DriveInfo(root);
            var pct = di.TotalSize == 0 ? 0 : (di.TotalSize - di.AvailableFreeSpace) * 100.0 / di.TotalSize;
            var warn = di.AvailableFreeSpace < 10L * 1024 * 1024 * 1024 ? "  ⚠ sắp đầy" : "";
            return $"{root} — trống {Fmt.Bytes(di.AvailableFreeSpace)} / {Fmt.Bytes(di.TotalSize)} (đã dùng {pct:0}%){warn}";
        }
        catch { return "—"; }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "App sẽ tạo một điểm khôi phục hệ thống, sau đó dọn thư mục tạm và áp dụng các tinh chỉnh an toàn.\n\n" +
            "Tiếp tục?",
            "Tối ưu nhanh", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        RunBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;

        try
        {
            Report("Đang tạo điểm khôi phục hệ thống…");
            var rp = await RestorePointService.EnsureAsync($"HPS Optimizer — tối ưu nhanh {DateTime.Now:dd/MM/yyyy HH:mm}");
            if (!rp)
            {
                var go = MessageBox.Show(
                    "Không tạo được điểm khôi phục (System Protection có thể đang tắt).\n\n" +
                    "Vẫn tiếp tục tối ưu? Bạn vẫn hoàn tác được từng mục trong tab Nhật ký.",
                    "Cảnh báo", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (go != MessageBoxResult.Yes) { Report("Đã huỷ."); return; }
            }

            long freed = 0;
            foreach (var target in CleanerService.BuildTargets().Where(t => SafeCleanIds.Contains(t.Id)))
            {
                Report($"Đang dọn: {target.Name}…");
                var result = await CleanerService.CleanAsync(target, new Progress<string>(Report));
                freed += result.BytesFreed;
            }

            var applied = 0;
            foreach (var tweak in TweakService.BuildAll().Where(t => t.Risk == RiskLevel.Safe))
            {
                await TweakService.RefreshStateAsync(tweak);
                if (tweak.IsApplied) continue;
                Report($"Đang áp dụng: {tweak.Name}…");
                await TweakService.ApplyAsync(tweak);
                applied++;
            }

            Report($"Xong. Giải phóng {Fmt.Bytes(freed)}, áp dụng {applied} tinh chỉnh. " +
                   "Đăng xuất hoặc khởi động lại để mọi thay đổi có hiệu lực.");
            Logger.Success($"Tối ưu nhanh hoàn tất: {Fmt.Bytes(freed)}, {applied} tinh chỉnh.");
            LoadSpecs();
        }
        catch (Exception ex)
        {
            Logger.Error("Tối ưu nhanh", ex);
            Report($"Lỗi: {ex.Message}");
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            RunBtn.IsEnabled = true;
        }
    }

    private void OpenRestore_Click(object sender, RoutedEventArgs e) => RestorePointService.OpenRestoreUi();

    private void Report(string text) => Dispatcher.Invoke(() =>
    {
        ProgressText.Text = text;
        MainWindow.SetStatus(text);
    });
}
