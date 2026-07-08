using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using HPSOptimizer.Core;

namespace HPSOptimizer.Services;

public sealed class ProcessRow : ObservableObject
{
    public required int Pid { get; init; }
    public required string Name { get; init; }

    private double _cpu;
    public double Cpu { get => _cpu; set { if (Set(ref _cpu, value)) OnPropertyChanged(nameof(CpuText)); } }
    public string CpuText => $"{Cpu:0.0}%";

    private long _memory;
    public long Memory { get => _memory; set { if (Set(ref _memory, value)) OnPropertyChanged(nameof(MemoryText)); } }
    public string MemoryText => Fmt.Bytes(Memory);
}

public sealed record SystemSnapshot(
    double CpuPercent,
    long RamUsed,
    long RamTotal,
    double DiskPercent,
    int Temperature)
{
    public double RamPercent => RamTotal == 0 ? 0 : RamUsed * 100.0 / RamTotal;
}

/// <summary>Thu thập CPU / RAM / Disk / nhiệt độ. Gọi Sample() mỗi giây.</summary>
public sealed class MonitorService : IDisposable
{
    private PerformanceCounter? _cpu;
    private PerformanceCounter? _disk;
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _procTimes = new();
    private bool _tempUnavailable;

    public MonitorService()
    {
        try
        {
            // "% Processor Utility" phản ánh đúng turbo boost; không có thì lùi về counter cổ điển.
            _cpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", readOnly: true);
            _ = _cpu.NextValue();
        }
        catch
        {
            try
            {
                _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
                _ = _cpu.NextValue();
            }
            catch (Exception ex) { Logger.Warn($"Không dùng được bộ đếm CPU: {ex.Message}"); }
        }

        try
        {
            _disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", readOnly: true);
            _ = _disk.NextValue();
        }
        catch (Exception ex) { Logger.Warn($"Không dùng được bộ đếm Disk: {ex.Message}"); }
    }

    public SystemSnapshot Sample()
    {
        double cpu = 0, disk = 0;
        try { cpu = Math.Clamp(_cpu?.NextValue() ?? 0, 0, 100); } catch { }
        try { disk = Math.Clamp(_disk?.NextValue() ?? 0, 0, 100); } catch { }

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        long total = 0, used = 0;
        if (GlobalMemoryStatusEx(ref mem))
        {
            total = (long)mem.ullTotalPhys;
            used = total - (long)mem.ullAvailPhys;
        }

        return new SystemSnapshot(cpu, used, total, disk, ReadTemperature());
    }

    /// <summary>Nhiệt độ CPU qua ACPI. Rất nhiều mainboard không expose — khi đó trả 0 và thôi hỏi lại.</summary>
    private int ReadTemperature()
    {
        if (_tempUnavailable) return 0;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementObject mo in searcher.Get())
                using (mo)
                {
                    // Giá trị là decikelvin.
                    var deciKelvin = Convert.ToDouble(mo["CurrentTemperature"]);
                    return (int)Math.Round(deciKelvin / 10.0 - 273.15);
                }
        }
        catch { /* rơi xuống dưới */ }

        _tempUnavailable = true;
        return 0;
    }

    /// <summary>Top tiến trình theo RAM, kèm %CPU tính từ delta giữa 2 lần gọi.</summary>
    public List<ProcessRow> TopProcesses(int take = 20)
    {
        var rows = new List<ProcessRow>();
        var now = DateTime.UtcNow;
        var cores = Environment.ProcessorCount;
        var seen = new HashSet<int>();

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    seen.Add(p.Id);
                    double cpuPercent = 0;
                    var cpuTime = p.TotalProcessorTime;

                    if (_procTimes.TryGetValue(p.Id, out var prev))
                    {
                        var wall = (now - prev.At).TotalSeconds;
                        if (wall > 0.05)
                            cpuPercent = (cpuTime - prev.Cpu).TotalSeconds / (wall * cores) * 100.0;
                    }
                    _procTimes[p.Id] = (cpuTime, now);

                    rows.Add(new ProcessRow
                    {
                        Pid = p.Id,
                        Name = p.ProcessName,
                        Cpu = Math.Clamp(cpuPercent, 0, 100),
                        Memory = p.WorkingSet64
                    });
                }
                catch
                {
                    // Idle / System / tiến trình vừa thoát → không đọc được, bỏ qua.
                }
            }
        }

        // Dọn cache PID đã chết.
        foreach (var dead in _procTimes.Keys.Where(k => !seen.Contains(k)).ToList())
            _procTimes.Remove(dead);

        return rows.OrderByDescending(r => r.Memory).Take(take).ToList();
    }

    public static bool Kill(int pid, out string error)
    {
        error = "";
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            Logger.Success($"Đã kết thúc tiến trình {p.ProcessName} (PID {pid}).");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Logger.Warn($"Không kết thúc được PID {pid}: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _disk?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
