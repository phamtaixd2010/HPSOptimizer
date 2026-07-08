using System.Windows;
using System.Windows.Controls;
using HPSOptimizer.Core;
using HPSOptimizer.Services;

namespace HPSOptimizer.Views;

public partial class DiskInfoView : UserControl
{
    private bool _loadedOnce;

    public DiskInfoView()
    {
        InitializeComponent();
        // Quét ổ mất 1–3 giây → chỉ chạy khi người dùng thực sự mở tab.
        IsVisibleChanged += async (_, e) =>
        {
            if ((bool)e.NewValue && !_loadedOnce) { _loadedOnce = true; await LoadAsync(); }
        };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        RefreshBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        StatusLine.Text = "Đang đọc thông tin ổ đĩa và cảm biến S.M.A.R.T…";

        try
        {
            var disks = await StorageService.GetPhysicalDisksAsync();
            DiskList.ItemsSource = disks;

            if (disks.Count == 0)
            {
                StatusLine.Text = "Không đọc được ổ nào. Cần quyền Administrator và dịch vụ Storage của Windows.";
                return;
            }

            var bad = disks.Where(d => d.PredictFailure ||
                                       d.Health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase)).ToList();
            StatusLine.Text = bad.Count > 0
                ? $"⚠ {bad.Count} ổ có dấu hiệu hỏng: {string.Join(", ", bad.Select(d => d.Name))}. Sao lưu ngay."
                : $"{disks.Count} ổ, tất cả đều báo khoẻ.";
        }
        catch (Exception ex)
        {
            Logger.Error("Đọc thông tin ổ đĩa", ex);
            StatusLine.Text = $"Lỗi: {ex.Message}";
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            RefreshBtn.IsEnabled = true;
        }
    }
}
