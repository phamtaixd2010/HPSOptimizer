using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HPSOptimizer.Core;
using HPSOptimizer.Services;

namespace HPSOptimizer.Views;

public partial class MonitorView : UserControl
{
    private const int HistorySize = 60;

    private readonly MonitorService _monitor = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _ramHistory = new();

    /// <summary>Danh sách tiến trình chỉ làm mới mỗi 3 giây — quét toàn bộ process tốn CPU trên máy yếu.</summary>
    private int _tick;

    public ObservableCollection<ProcessRow> Processes { get; } = new();

    public MonitorView()
    {
        InitializeComponent();
        DataContext = this;

        _timer.Tick += OnTick;

        // Chỉ chạy timer khi tab đang hiển thị: tiết kiệm CPU.
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) _timer.Start();
            else _timer.Stop();
        };

        Unloaded += (_, _) => { _timer.Stop(); _monitor.Dispose(); };
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var s = _monitor.Sample();

        CpuValue.Text = Fmt.Percent(s.CpuPercent);
        CpuBar.Value = s.CpuPercent;

        RamValue.Text = Fmt.Percent(s.RamPercent);
        RamBar.Value = s.RamPercent;
        RamDetail.Text = $"{Fmt.Bytes(s.RamUsed)} / {Fmt.Bytes(s.RamTotal)}";

        DiskValue.Text = Fmt.Percent(s.DiskPercent);
        DiskBar.Value = s.DiskPercent;

        TempValue.Text = Fmt.Temp(s.Temperature);

        Push(_cpuHistory, s.CpuPercent);
        Push(_ramHistory, s.RamPercent);
        RedrawChart();

        if (_tick++ % 3 == 0) RefreshProcesses();
    }

    private static void Push(Queue<double> q, double value)
    {
        q.Enqueue(value);
        while (q.Count > HistorySize) q.Dequeue();
    }

    private void RefreshProcesses()
    {
        var rows = _monitor.TopProcesses(25);

        // Cập nhật tại chỗ để không phá selection của người dùng.
        var byPid = Processes.ToDictionary(p => p.Pid);
        foreach (var r in rows)
        {
            if (byPid.TryGetValue(r.Pid, out var existing))
            {
                existing.Cpu = r.Cpu;
                existing.Memory = r.Memory;
                byPid.Remove(r.Pid);
            }
            else Processes.Add(r);
        }
        foreach (var gone in byPid.Values) Processes.Remove(gone);
    }

    private void RedrawChart()
    {
        DrawLine(CpuLine, _cpuHistory);
        DrawLine(RamLine, _ramHistory);
    }

    private void DrawLine(System.Windows.Shapes.Polyline line, Queue<double> data)
    {
        var w = ChartCanvas.ActualWidth;
        var h = ChartCanvas.ActualHeight;
        if (w <= 0 || h <= 0 || data.Count < 2) return;

        var points = new System.Windows.Media.PointCollection(data.Count);
        var stepX = w / (HistorySize - 1);
        var i = 0;
        foreach (var v in data)
        {
            points.Add(new Point(i * stepX, h - v / 100.0 * (h - 6) - 3));
            i++;
        }
        line.Points = points;
    }

    private void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawChart();

    private void Kill_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcessRow row)
        {
            MessageBox.Show("Chọn một tiến trình trong bảng trước.", "Giám sát",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(
                $"Kết thúc {row.Name} (PID {row.Pid}) và toàn bộ tiến trình con?\n\n" +
                "Dữ liệu chưa lưu trong ứng dụng đó sẽ mất.",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (!MonitorService.Kill(row.Pid, out var error))
            MessageBox.Show($"Không kết thúc được: {error}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        else
            Processes.Remove(row);
    }
}
