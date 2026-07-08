using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using HPSOptimizer.Core;
using HPSOptimizer.Services;

namespace HPSOptimizer.Views;

public partial class TweakView : UserControl
{
    public ObservableCollection<Tweak> Tweaks { get; } = new();

    public TweakView()
    {
        InitializeComponent();
        DataContext = this;
        foreach (var t in TweakService.BuildAll()) Tweaks.Add(t);
        Loaded += async (_, _) => await RefreshAll();
    }

    private async Task RefreshAll()
    {
        foreach (var t in Tweaks) await TweakService.RefreshStateAsync(t);
        var applied = Tweaks.Count(t => t.IsApplied);
        StatusLine.Text = $"{applied}/{Tweaks.Count} tinh chỉnh đang được áp dụng.";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAll();

    private void SelectSafe_Click(object sender, RoutedEventArgs e)
    {
        foreach (var t in Tweaks) t.Selected = t.Risk == RiskLevel.Safe && !t.IsApplied;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var chosen = Tweaks.Where(t => t.Selected).ToList();
        if (chosen.Count == 0) { Info("Chưa chọn tinh chỉnh nào."); return; }

        var risky = chosen.Where(t => t.Risk == RiskLevel.Medium).ToList();
        var msg = $"Áp dụng {chosen.Count} tinh chỉnh?\n\n" +
                  string.Join("\n", chosen.Select(t => $"  • {t.Name}"));
        if (risky.Count > 0)
            msg += "\n\nCác mục cần cân nhắc:\n" + string.Join("\n", risky.Select(t => $"  ⚠ {t.Name}"));
        msg += "\n\nApp sẽ tạo điểm khôi phục trước.";

        if (MessageBox.Show(msg, "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await Run(async () =>
        {
            await RestorePointService.EnsureAsync($"HPS Optimizer — tinh chỉnh {DateTime.Now:dd/MM HH:mm}");
            foreach (var t in chosen)
            {
                try { await TweakService.ApplyAsync(t); t.Selected = false; }
                catch (Exception ex) { Logger.Error($"Áp dụng {t.Name}", ex); }
            }
            var needRestart = chosen.Any(t => t.NeedsRestart);
            await RefreshAll();
            if (needRestart)
                StatusLine.Text += "  Cần đăng xuất hoặc khởi động lại để thấy hiệu quả đầy đủ.";
        });
    }

    private async void Revert_Click(object sender, RoutedEventArgs e)
    {
        var chosen = Tweaks.Where(t => t.Selected).ToList();
        if (chosen.Count == 0) { Info("Chưa chọn tinh chỉnh nào."); return; }

        if (MessageBox.Show($"Trả {chosen.Count} tinh chỉnh về giá trị ban đầu?", "Hoàn tác",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        await Run(async () =>
        {
            foreach (var t in chosen)
            {
                try { await TweakService.RevertAsync(t); t.Selected = false; }
                catch (Exception ex) { Logger.Error($"Hoàn tác {t.Name}", ex); }
            }
            await RefreshAll();
        });
    }

    private async Task Run(Func<Task> work)
    {
        ApplyBtn.IsEnabled = RevertBtn.IsEnabled = false;
        try { await work(); }
        finally { ApplyBtn.IsEnabled = RevertBtn.IsEnabled = true; }
    }

    private static void Info(string m) =>
        MessageBox.Show(m, "Tinh chỉnh", MessageBoxButton.OK, MessageBoxImage.Information);
}
