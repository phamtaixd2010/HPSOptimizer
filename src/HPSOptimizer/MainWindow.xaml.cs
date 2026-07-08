using System.Windows;
using System.Windows.Media;
using HPSOptimizer.Core;

namespace HPSOptimizer;

public partial class MainWindow : Window
{
    /// <summary>Cho các view con báo trạng thái lên thanh dưới cùng.</summary>
    public static void SetStatus(string text)
    {
        if (Application.Current?.MainWindow is MainWindow w)
            w.Dispatcher.Invoke(() => w.StatusText.Text = text);
    }

    public MainWindow()
    {
        InitializeComponent();

        if (!App.IsAdmin)
        {
            AdminBadge.Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
            AdminBadgeText.Text = "THIẾU QUYỀN ADMIN";
            AdminBadgeText.Foreground = Brushes.White;
        }

        Logger.Written += item =>
        {
            if (item.Level is LogLevel.Error or LogLevel.Warn or LogLevel.Success)
                SetStatus($"[{item.TimeText}] {item.Message}");
        };
    }
}
