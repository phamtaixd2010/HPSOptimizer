using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using HPSOptimizer.Core;
using HPSOptimizer.Services;

namespace HPSOptimizer.Views;

public partial class DiskUsageView : UserControl
{
    private CancellationTokenSource? _cts;

    public DiskUsageView()
    {
        InitializeComponent();
        PathBox.Text = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        // OpenFolderDialog có sẵn từ .NET 8 WPF, không cần WinForms.
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Chọn thư mục cần phân tích" };
        if (dlg.ShowDialog() == true) PathBox.Text = dlg.FolderName;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        var root = PathBox.Text.Trim();
        if (!Directory.Exists(root)) { Warn($"Không tìm thấy thư mục: {root}"); return; }

        var depth = DepthBox.SelectedIndex + 1;
        await RunAsync(async ct =>
        {
            var sw = Stopwatch.StartNew();
            var node = await DiskUsageService.ScanAsync(root, depth, new Progress<string>(Status), ct);
            Tree.ItemsSource = new[] { node };
            Status($"Xong sau {sw.Elapsed.TotalSeconds:0.0}s — {Fmt.Bytes(node.Size)} trong {node.FileCount:N0} file.");
        });
    }

    private async void Dup_Click(object sender, RoutedEventArgs e)
    {
        var root = PathBox.Text.Trim();
        if (!Directory.Exists(root)) { Warn($"Không tìm thấy thư mục: {root}"); return; }

        if (root.Length <= 3 && MessageBox.Show(
                "Bạn đang tìm file trùng trên cả một ổ đĩa. Việc này có thể mất rất lâu và đọc nhiều dữ liệu.\n\nTiếp tục?",
                "Cảnh báo", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        await RunAsync(async ct =>
        {
            var groups = await DiskUsageService.FindDuplicatesAsync(root, 1024 * 1024, new Progress<string>(Status), ct);
            DupList.ItemsSource = groups;
            var wasted = groups.Sum(g => g.Wasted);
            DupSummary.Text = groups.Count == 0
                ? "Không tìm thấy file trùng nào ≥ 1 MB."
                : $"{groups.Count} nhóm trùng — có thể thu hồi tối đa {Fmt.Bytes(wasted)}. " +
                  "App chỉ liệt kê, việc xoá do bạn tự quyết định.";
            Status(DupSummary.Text);
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Status("Đang dừng…");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = Tree.SelectedItem is UsageNode n ? n.FullPath : PathBox.Text;
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Logger.Error("Mở Explorer", ex); }
    }

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        _cts = new CancellationTokenSource();
        ScanBtn.IsEnabled = DupBtn.IsEnabled = false;
        CancelBtn.IsEnabled = true;
        Progress.Visibility = Visibility.Visible;

        try { await work(_cts.Token); }
        catch (OperationCanceledException) { Status("Đã dừng theo yêu cầu."); }
        catch (Exception ex) { Logger.Error("Phân tích dung lượng", ex); Status($"Lỗi: {ex.Message}"); }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            ScanBtn.IsEnabled = DupBtn.IsEnabled = true;
            CancelBtn.IsEnabled = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void Status(string text) => Dispatcher.Invoke(() =>
    {
        StatusLine.Text = text;
        MainWindow.SetStatus(text);
    });

    private static void Warn(string m) => MessageBox.Show(m, "Phân tích dung lượng",
        MessageBoxButton.OK, MessageBoxImage.Warning);
}
