using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using HPSOptimizer.Core;
using HPSOptimizer.Services;

namespace HPSOptimizer.Views;

public partial class StartupView : UserControl
{
    public ObservableCollection<StartupItem> Items { get; } = new();

    public StartupView()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        Items.Clear();
        foreach (var i in StartupService.Load()) Items.Add(i);
        StatusLine.Text = $"{Items.Count} mục khởi động.";
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => Reload();

    private void Disable_Click(object sender, RoutedEventArgs e)
    {
        var chosen = Items.Where(i => i.Selected).ToList();
        if (chosen.Count == 0)
        {
            MessageBox.Show("Chưa chọn mục nào.", "Khởi động", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var msg = $"Tắt {chosen.Count} mục khởi động?\n\n" +
                  string.Join("\n", chosen.Select(i => $"  • {i.Name}")) +
                  "\n\nCó thể bật lại bất cứ lúc nào ở tab Nhật ký.";
        if (MessageBox.Show(msg, "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var ok = 0;
        foreach (var item in chosen)
        {
            try { StartupService.Disable(item); ok++; }
            catch (Exception ex) { Logger.Error($"Tắt startup {item.Name}", ex); }
        }

        Reload();
        StatusLine.Text = $"Đã tắt {ok}/{chosen.Count} mục.";
    }

    private void TaskMgr_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Error("Mở Task Manager", ex); }
    }
}
