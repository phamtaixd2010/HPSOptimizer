using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using HPSOptimizer.Core;
using HPSOptimizer.Services;

namespace HPSOptimizer.Views;

public sealed class VolumeRow
{
    public required string Letter { get; init; }
    public required string Label { get; init; }
    public required string FileSystem { get; init; }
    public required string MediaKind { get; init; }
    public required bool IsSsd { get; init; }
    public required long Total { get; init; }
    public required long Free { get; init; }
    public string SpaceText => $"{Fmt.Bytes(Free)} trống / {Fmt.Bytes(Total)}";
}

public partial class DiskToolsView : UserControl
{
    private bool _loadedOnce;
    private List<PhysicalDiskInfo> _disks = new();
    private List<PartitionInfo> _partitions = new();

    public DiskToolsView()
    {
        InitializeComponent();
        IsVisibleChanged += async (_, e) =>
        {
            if ((bool)e.NewValue && !_loadedOnce) { _loadedOnce = true; await ReloadAsync(); }
        };
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        Status("Đang đọc danh sách ổ và phân vùng…");
        try
        {
            _disks = await StorageService.GetPhysicalDisksAsync();
            _partitions = await StorageService.GetPartitionsAsync();

            PartGrid.ItemsSource = _partitions;
            VolumeGrid.ItemsSource = BuildVolumes();

            Status($"{_disks.Count} ổ vật lý, {_partitions.Count} phân vùng.");
        }
        catch (Exception ex)
        {
            Logger.Error("Đọc ổ đĩa", ex);
            Status($"Lỗi: {ex.Message}");
        }
    }

    private List<VolumeRow> BuildVolumes()
    {
        var rows = new List<VolumeRow>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            var letter = drive.Name[..1];
            var part = _partitions.FirstOrDefault(p => p.Letter.Equals(letter, StringComparison.OrdinalIgnoreCase));
            var disk = part is null ? null : _disks.FirstOrDefault(d => d.Number == part.Disk);

            rows.Add(new VolumeRow
            {
                Letter = $"{letter}:",
                Label = drive.VolumeLabel,
                FileSystem = drive.DriveFormat,
                MediaKind = disk is null
                    ? drive.DriveType.ToString()
                    : $"{disk.MediaType} · {disk.BusType}",
                IsSsd = disk?.IsSsd ?? false,
                Total = drive.TotalSize,
                Free = drive.AvailableFreeSpace,
            });
        }
        return rows;
    }

    // ---------------------------------------------------------------- bảo trì

    private async void Optimize_Click(object sender, RoutedEventArgs e)
    {
        if (VolumeGrid.SelectedItem is not VolumeRow vol) { Info("Chọn một ổ trong bảng trên."); return; }

        var op = vol.IsSsd ? "TRIM" : "chống phân mảnh";
        if (MessageBox.Show(
                $"Chạy {op} cho ổ {vol.Letter} ({vol.MediaKind})?\n\n" +
                "Việc này có thể mất vài phút và làm máy chậm trong lúc chạy. Không mất dữ liệu.",
                "Tối ưu ổ đĩa", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        await WithProgress(() => StorageService.OptimizeAsync(vol.Letter[..1], vol.IsSsd, new Progress<string>(Status)));
        await ReloadAsync();
    }

    private async void ScanVolume_Click(object sender, RoutedEventArgs e)
    {
        if (VolumeGrid.SelectedItem is not VolumeRow vol) { Info("Chọn một ổ trong bảng trên."); return; }

        await WithProgress(async () =>
        {
            var result = await StorageService.ScanVolumeAsync(vol.Letter[..1]);
            var friendly = result.Contains("NoErrorsFound", StringComparison.OrdinalIgnoreCase)
                ? "Không phát hiện lỗi."
                : $"Kết quả: {result}";
            Dispatcher.Invoke(() => MessageBox.Show($"Ổ {vol.Letter}\n\n{friendly}", "Quét lỗi ổ đĩa",
                MessageBoxButton.OK, MessageBoxImage.Information));
            return true;
        });
    }

    // ---------------------------------------------------------------- phân vùng

    private PartitionInfo? Selected()
    {
        if (PartGrid.SelectedItem is not PartitionInfo p)
        {
            Info("Chọn một phân vùng trong bảng.");
            return null;
        }
        if (p.IsProtected)
        {
            MessageBox.Show(
                $"{p.Display} là phân vùng {p.Type} — boot / system / recovery.\n\n" +
                "App từ chối thao tác lên phân vùng này để bạn không làm máy mất khả năng khởi động.\n" +
                "Nếu thực sự cần, hãy dùng Disk Management hoặc diskpart và tự chịu trách nhiệm.",
                "Bị chặn", MessageBoxButton.OK, MessageBoxImage.Stop);
            return null;
        }
        return p;
    }

    private async void Resize_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } p) return;
        if (p.MaxSize <= 0) { Info("Windows không cho biết khoảng co giãn hợp lệ của phân vùng này."); return; }

        var currentGb = p.Size / 1024.0 / 1024 / 1024;
        var input = Dialogs.Prompt(Window.GetWindow(this), "Đổi kích thước phân vùng",
            $"{p.Display}\n" +
            $"Hiện tại: {p.SizeText}\n" +
            $"Khoảng hợp lệ: {p.ResizeRangeText}\n\n" +
            "Nhập kích thước mới, đơn vị GB:",
            $"{currentGb:0.##}");

        if (input is null) return;
        if (!double.TryParse(input, out var gb) || gb <= 0) { Info("Giá trị không hợp lệ."); return; }

        var bytes = (long)(gb * 1024 * 1024 * 1024);
        if (bytes < p.MinSize || bytes > p.MaxSize)
        {
            Info($"Kích thước phải nằm trong khoảng {p.ResizeRangeText}.");
            return;
        }

        var shrinking = bytes < p.Size;
        if (!Dialogs.ConfirmByTyping(Window.GetWindow(this), "Xác nhận đổi kích thước",
                $"{(shrinking ? "THU NHỎ" : "MỞ RỘNG")} {p.Display}\n" +
                $"{p.SizeText}  →  {Fmt.Bytes(bytes)}\n\n" +
                "Thao tác này ghi trực tiếp lên bảng phân vùng. Nếu mất điện giữa chừng, dữ liệu có thể hỏng.\n" +
                "Hãy chắc chắn bạn đã sao lưu.",
                "TOI DA SAO LUU")) return;

        await WithProgress(() => StorageService.ShrinkOrExtendAsync(p, bytes));
        await ReloadAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } p) return;

        var phrase = string.IsNullOrWhiteSpace(p.Letter) ? $"XOA DISK{p.Disk}P{p.Part}" : $"XOA {p.Letter}";
        if (!Dialogs.ConfirmByTyping(Window.GetWindow(this), "Xoá phân vùng",
                $"XOÁ VĨNH VIỄN {p.Display}\n" +
                $"Dung lượng: {p.SizeText}   Nhãn: {p.Label}\n\n" +
                "Toàn bộ dữ liệu trên phân vùng này sẽ biến mất. Không có thùng rác. Không hoàn tác được.",
                phrase)) return;

        await WithProgress(() => StorageService.DeleteAsync(p));
        await ReloadAsync();
    }

    private async void Format_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } p) return;
        if (string.IsNullOrWhiteSpace(p.Letter)) { Info("Phân vùng chưa có ký tự ổ. Gán ký tự trước đã."); return; }

        var fs = Dialogs.Prompt(Window.GetWindow(this), "Format phân vùng",
            $"{p.Display}\n\nĐịnh dạng file system (NTFS / exFAT / FAT32):", p.FileSystem is "" ? "NTFS" : p.FileSystem);
        if (fs is null) return;
        if (fs is not ("NTFS" or "exFAT" or "FAT32")) { Info("Chỉ hỗ trợ NTFS, exFAT, FAT32."); return; }

        var label = Dialogs.Prompt(Window.GetWindow(this), "Format phân vùng", "Nhãn ổ đĩa (có thể để trống):", p.Label);
        if (label is null) return;

        if (!Dialogs.ConfirmByTyping(Window.GetWindow(this), "Format phân vùng",
                $"FORMAT {p.LetterText} sang {fs}\n" +
                $"Dung lượng: {p.SizeText}\n\n" +
                "Mọi file trên ổ này sẽ bị xoá sạch. Không hoàn tác được.",
                $"FORMAT {p.Letter}")) return;

        await WithProgress(() => StorageService.FormatAsync(p, fs, label));
        await ReloadAsync();
    }

    private async void Letter_Click(object sender, RoutedEventArgs e)
    {
        if (PartGrid.SelectedItem is not PartitionInfo p) { Info("Chọn một phân vùng."); return; }

        var input = Dialogs.Prompt(Window.GetWindow(this), "Gán ký tự ổ",
            $"{p.Display}\n\nNhập một chữ cái (ví dụ E):", string.IsNullOrEmpty(p.Letter) ? "E" : p.Letter);
        if (string.IsNullOrWhiteSpace(input)) return;

        var letter = char.ToUpperInvariant(input[0]);
        if (letter is < 'D' or > 'Z') { Info("Chỉ nhận chữ cái từ D đến Z."); return; }

        await WithProgress(() => StorageService.AssignLetterAsync(p, letter));
        await ReloadAsync();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var freeDisks = _disks.Where(d => !d.IsBoot && !d.IsSystem).ToList();
        if (freeDisks.Count == 0)
        {
            Info("Chỉ có ổ hệ thống trên máy này. App không tạo phân vùng trên ổ chứa Windows.");
            return;
        }

        var diskInput = Dialogs.Prompt(Window.GetWindow(this), "Tạo phân vùng mới",
            "Ổ vật lý khả dụng (không phải ổ hệ thống):\n\n" +
            string.Join("\n", freeDisks.Select(d => $"  Disk {d.Number} — {d.Name} ({d.SizeText})")) +
            "\n\nNhập số hiệu Disk:", freeDisks[0].Number.ToString());
        if (diskInput is null || !int.TryParse(diskInput, out var diskNumber)) return;
        if (freeDisks.All(d => d.Number != diskNumber)) { Info("Số hiệu Disk không hợp lệ hoặc là ổ hệ thống."); return; }

        var sizeInput = Dialogs.Prompt(Window.GetWindow(this), "Tạo phân vùng mới",
            "Kích thước (GB). Gõ MAX để dùng toàn bộ chỗ trống:", "MAX");
        if (sizeInput is null) return;

        var useMax = sizeInput.Equals("MAX", StringComparison.OrdinalIgnoreCase);
        long bytes = 0;
        if (!useMax)
        {
            if (!double.TryParse(sizeInput, out var gb) || gb <= 0) { Info("Kích thước không hợp lệ."); return; }
            bytes = (long)(gb * 1024 * 1024 * 1024);
        }

        var label = Dialogs.Prompt(Window.GetWindow(this), "Tạo phân vùng mới", "Nhãn ổ đĩa:", "DATA");
        if (label is null) return;

        if (!Dialogs.ConfirmByTyping(Window.GetWindow(this), "Tạo phân vùng mới",
                $"Tạo phân vùng {(useMax ? "chiếm hết chỗ trống" : Fmt.Bytes(bytes))} trên Disk {diskNumber}, " +
                "định dạng NTFS.\n\nThao tác ghi lên bảng phân vùng của ổ đó.",
                $"TAO DISK{diskNumber}")) return;

        await WithProgress(() => StorageService.CreatePartitionAsync(diskNumber, bytes, useMax, "NTFS", label));
        await ReloadAsync();
    }

    private void OpenDiskMgmt_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("diskmgmt.msc") { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Error("Mở Disk Management", ex); }
    }

    // ---------------------------------------------------------------- tiện ích

    private async Task WithProgress(Func<Task<bool>> work)
    {
        MaintProgress.Visibility = Visibility.Visible;
        OptimizeBtn.IsEnabled = ScanBtn.IsEnabled = false;
        try
        {
            var ok = await work();
            if (!ok) Status("Thao tác thất bại — xem chi tiết ở tab Nhật ký.");
        }
        catch (Exception ex)
        {
            Logger.Error("Thao tác ổ đĩa", ex);
            MessageBox.Show(ex.Message, "Thao tác bị từ chối", MessageBoxButton.OK, MessageBoxImage.Stop);
        }
        finally
        {
            MaintProgress.Visibility = Visibility.Collapsed;
            OptimizeBtn.IsEnabled = ScanBtn.IsEnabled = true;
        }
    }

    private void Status(string text) => Dispatcher.Invoke(() =>
    {
        StatusLine.Text = text;
        MainWindow.SetStatus(text);
    });

    private static void Info(string m) => MessageBox.Show(m, "Ổ đĩa", MessageBoxButton.OK, MessageBoxImage.Information);
}
