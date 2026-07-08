using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using HPSOptimizer.Core;
using HPSOptimizer.Services;

namespace HPSOptimizer.Views;

public partial class CleanView : UserControl
{
    public ObservableCollection<CleanTarget> Targets { get; } = new();

    public CleanView()
    {
        InitializeComponent();
        DataContext = this;
        foreach (var t in CleanerService.BuildTargets()) Targets.Add(t);
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        await WithBusy(async () =>
        {
            long total = 0;
            foreach (var t in Targets)
            {
                Status($"Đang quét {t.Name}…");
                await CleanerService.ScanAsync(t);
                if (t.Selected) total += Math.Max(t.Size, 0);
            }
            TotalText.Text = $"Có thể thu hồi: {Fmt.Bytes(total)}";
            Status("Quét xong. Tick các mục muốn dọn rồi bấm \"Dọn các mục đã chọn\".");
        });
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var chosen = Targets.Where(t => t.Selected).ToList();
        if (chosen.Count == 0)
        {
            MessageBox.Show("Chưa chọn mục nào.", "Dọn rác", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var risky = chosen.Where(t => t.Warning is not null).ToList();
        var msg = $"Sẽ dọn {chosen.Count} mục:\n\n" +
                  string.Join("\n", chosen.Select(t => $"  • {t.Name}  ({t.SizeText})"));
        if (risky.Count > 0)
            msg += "\n\nCẢNH BÁO không thể hoàn tác:\n" +
                   string.Join("\n", risky.Select(t => $"  ⚠ {t.Name}: {t.Warning}"));
        msg += "\n\nTiếp tục?";

        if (MessageBox.Show(msg, "Xác nhận dọn rác", MessageBoxButton.YesNo,
                risky.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await WithBusy(async () =>
        {
            long freed = 0;
            int skipped = 0;
            foreach (var t in chosen)
            {
                var r = await CleanerService.CleanAsync(t, new Progress<string>(Status));
                freed += r.BytesFreed;
                skipped += r.FilesSkipped;
            }
            TotalText.Text = $"Đã giải phóng: {Fmt.Bytes(freed)}";
            Status($"Hoàn tất. Giải phóng {Fmt.Bytes(freed)}." +
                   (skipped > 0 ? $" Bỏ qua {skipped} file đang bị khoá — khởi động lại rồi dọn tiếp nếu cần." : ""));
        });
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var t in Targets) t.Selected = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var t in Targets) t.Selected = false;
    }

    private async Task WithBusy(Func<Task> work)
    {
        ScanBtn.IsEnabled = CleanBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        try { await work(); }
        catch (Exception ex) { Logger.Error("Dọn rác", ex); Status($"Lỗi: {ex.Message}"); }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            ScanBtn.IsEnabled = CleanBtn.IsEnabled = true;
        }
    }

    private void Status(string text) => Dispatcher.Invoke(() =>
    {
        StatusLine.Text = text;
        MainWindow.SetStatus(text);
    });
}
