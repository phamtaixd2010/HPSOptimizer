using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using HPSOptimizer.Core;
using HPSOptimizer.Services;

namespace HPSOptimizer.Views;

public partial class ServiceView : UserControl
{
    public ObservableCollection<ServiceItem> Items { get; } = new();

    public ServiceView()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        Items.Clear();
        foreach (var s in ServiceTweakService.LoadInstalled()) Items.Add(s);
        var pending = Items.Count(i => !i.IsOptimized);
        StatusLine.Text = $"{Items.Count} dịch vụ có trên máy này. {pending} mục chưa theo khuyến nghị.";
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => Reload();

    private void SelectHigh_Click(object sender, RoutedEventArgs e)
    {
        foreach (var i in Items) i.Selected = i.Impact == "Cao" && !i.IsOptimized;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var chosen = Items.Where(i => i.Selected && !i.IsOptimized).ToList();
        if (chosen.Count == 0)
        {
            MessageBox.Show("Chưa chọn mục nào cần đổi.", "Dịch vụ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var msg = $"Đổi chế độ khởi động của {chosen.Count} dịch vụ?\n\n" +
                  string.Join("\n", chosen.Select(i => $"  • {i.DisplayName}: {i.StartMode} → {i.Recommended}")) +
                  "\n\nApp sẽ tạo điểm khôi phục trước. Hoàn tác được ở tab Nhật ký.";
        if (MessageBox.Show(msg, "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        ApplyBtn.IsEnabled = false;
        try
        {
            StatusLine.Text = "Đang tạo điểm khôi phục…";
            await RestorePointService.EnsureAsync($"HPS Optimizer — đổi dịch vụ {DateTime.Now:dd/MM HH:mm}");

            var ok = 0;
            foreach (var item in chosen)
            {
                try
                {
                    await Task.Run(() => ServiceTweakService.Apply(item));
                    ok++;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Đổi dịch vụ {item.Name}", ex);
                }
                item.Selected = false;
            }

            Reload();
            StatusLine.Text = $"Đã đổi {ok}/{chosen.Count} dịch vụ. Khởi động lại để chắc chắn có hiệu lực.";
        }
        finally { ApplyBtn.IsEnabled = true; }
    }

    private void OpenMsc_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Error("Mở services.msc", ex); }
    }
}
