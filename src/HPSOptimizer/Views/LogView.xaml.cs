using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using HPSOptimizer.Core;
using HPSOptimizer.Services;

namespace HPSOptimizer.Views;

public partial class LogView : UserControl
{
    public ObservableCollection<UndoEntry> Entries { get; } = new();
    public ObservableCollection<LogItem> Logs { get; } = new();

    public LogView()
    {
        InitializeComponent();
        DataContext = this;

        Logger.Written += OnLogWritten;
        Unloaded += (_, _) => Logger.Written -= OnLogWritten;

        Loaded += (_, _) => Reload();
    }

    // InvokeAsync(Action) chứ không phải BeginInvoke — BeginInvoke chỉ nhận Delegate,
    // truyền thẳng lambda vào là CS1660.
    private void OnLogWritten(LogItem item) => Dispatcher.InvokeAsync(() =>
    {
        Logs.Insert(0, item);
        while (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1);
    });

    private void Reload()
    {
        Entries.Clear();
        foreach (var e in UndoJournal.Load()) Entries.Add(e);
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => Reload();

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (UndoGrid.SelectedItem is not UndoEntry entry)
        {
            MessageBox.Show("Chọn một dòng trong nhật ký.", "Hoàn tác", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (entry.Reverted)
        {
            MessageBox.Show("Mục này đã được hoàn tác rồi.", "Hoàn tác", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"Hoàn tác \"{entry.Title}\"?\n\nGiá trị sẽ trở về: {entry.OldValue ?? "(xoá hẳn)"}",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        TryRevert(entry);
        Reload();
    }

    private void RevertAll_Click(object sender, RoutedEventArgs e)
    {
        var pending = Entries.Where(x => !x.Reverted).ToList();
        if (pending.Count == 0) { MessageBox.Show("Không có gì để hoàn tác.", "Hoàn tác"); return; }

        if (MessageBox.Show(
                $"Trả toàn bộ {pending.Count} thay đổi về nguyên trạng?\n\n" +
                "Bao gồm registry, dịch vụ, mục khởi động và power plan. " +
                "Việc dọn rác đã thực hiện thì không lấy lại được.",
                "Hoàn tác tất cả", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var ok = 0;
        // Hoàn tác theo thứ tự ngược thời gian (mới nhất trước) để tránh chồng chéo.
        foreach (var entry in pending)
            if (TryRevert(entry)) ok++;

        Reload();
        MessageBox.Show($"Đã hoàn tác {ok}/{pending.Count} mục. Khởi động lại để chắc chắn có hiệu lực.",
            "Hoàn tác", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool TryRevert(UndoEntry entry)
    {
        try
        {
            TweakService.RevertEntry(entry);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Hoàn tác '{entry.Title}'", ex);
            return false;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Xoá nhật ký hoàn tác?\n\nCác thay đổi đã áp dụng vẫn giữ nguyên, " +
                "nhưng bạn sẽ mất khả năng hoàn tác chúng bằng app.",
                "Xoá nhật ký", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        UndoJournal.Clear();
        Reload();
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.DataDir}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Error("Mở thư mục log", ex); }
    }
}
