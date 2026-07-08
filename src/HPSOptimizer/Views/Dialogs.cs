using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HPSOptimizer.Views;

/// <summary>
/// Hộp thoại dựng bằng code (không XAML) cho hai nhu cầu: nhập một giá trị,
/// và xác nhận thao tác nguy hiểm bằng cách gõ đúng một chuỗi.
/// </summary>
public static class Dialogs
{
    public static string? Prompt(Window? owner, string title, string message, string defaultValue = "")
    {
        var box = new TextBox
        {
            Text = defaultValue,
            Height = 30,
            Padding = new Thickness(6, 0, 6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };

        string? result = null;
        var win = Build(owner, title, message, box, "OK", () => result = box.Text.Trim(), _ => true);
        box.Focus();
        box.SelectAll();
        win.ShowDialog();
        return result;
    }

    /// <summary>Trả true chỉ khi người dùng gõ chính xác <paramref name="phrase"/>.</summary>
    public static bool ConfirmByTyping(Window? owner, string title, string message, string phrase)
    {
        var hint = new TextBlock
        {
            Text = $"Gõ chính xác:  {phrase}",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)),
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        var box = new TextBox
        {
            Height = 30,
            Padding = new Thickness(6, 0, 6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var panel = new StackPanel();
        panel.Children.Add(hint);
        panel.Children.Add(box);

        var ok = false;
        var win = Build(owner, title, message, panel, "Tôi hiểu rủi ro, thực hiện",
            () => ok = true,
            _ => string.Equals(box.Text.Trim(), phrase, StringComparison.Ordinal));

        box.Focus();
        win.ShowDialog();
        return ok;
    }

    private static Window Build(Window? owner, string title, string message, UIElement content,
                                string okText, Action onOk, Func<object?, bool> canOk)
    {
        var win = new Window
        {
            Title = title,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner ?? Application.Current?.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFC)),
            ShowInTaskbar = false
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        });
        root.Children.Add(content);

        var okBtn = new Button { Content = okText, MinWidth = 180, IsEnabled = canOk(null) };
        var cancelBtn = new Button { Content = "Huỷ", MinWidth = 90, IsCancel = true };
        if (Application.Current?.TryFindResource("GhostButton") is Style ghost) cancelBtn.Style = ghost;
        if (Application.Current?.TryFindResource("DangerButton") is Style danger && okText.Length > 12) okBtn.Style = danger;

        okBtn.Click += (_, _) => { onOk(); win.DialogResult = true; };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);
        root.Children.Add(buttons);
        win.Content = root;

        // Bật/tắt nút OK theo điều kiện, kiểm tra lại mỗi khi nội dung thay đổi.
        var timer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(150), System.Windows.Threading.DispatcherPriority.Input,
            (_, _) => okBtn.IsEnabled = canOk(null), win.Dispatcher);
        win.Closed += (_, _) => timer.Stop();
        timer.Start();

        return win;
    }
}
