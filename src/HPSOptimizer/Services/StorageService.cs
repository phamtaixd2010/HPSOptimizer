using System.Text.Json;
using HPSOptimizer.Core;

namespace HPSOptimizer.Services;

public sealed class PhysicalDiskInfo
{
    public int Number { get; init; }
    public string Name { get; init; } = "";
    public string MediaType { get; init; } = "";
    public string BusType { get; init; } = "";
    public long Size { get; init; }
    public string Health { get; init; } = "";
    public string OperationalStatus { get; init; } = "";
    public string Serial { get; init; } = "";
    public string Firmware { get; init; } = "";
    public int Temperature { get; init; } = -1;
    public long PowerOnHours { get; init; } = -1;
    public int Wear { get; init; } = -1;
    public long ReadErrors { get; init; } = -1;
    public long WriteErrors { get; init; } = -1;
    public bool IsBoot { get; init; }
    public bool IsSystem { get; init; }
    public string PartitionStyle { get; init; } = "";
    /// <summary>SMART dự báo hỏng (MSStorageDriver_FailurePredictStatus).</summary>
    public bool PredictFailure { get; init; }

    public string SizeText => Fmt.Bytes(Size);
    public string TempText => Fmt.Temp(Temperature);
    public string HoursText => Fmt.Hours(PowerOnHours);
    public string WearText => Wear < 0 ? "—" : $"{Wear}% đã hao mòn";
    public string ErrorsText => ReadErrors < 0 ? "—" : $"đọc {ReadErrors:N0} / ghi {WriteErrors:N0}";
    public string BadgeText => IsBoot || IsSystem ? "Ổ HỆ THỐNG" : "";

    /// <summary>Kết luận sức khoẻ bằng tiếng người, ưu tiên SMART predict-fail hơn HealthStatus.</summary>
    public string Verdict
    {
        get
        {
            if (PredictFailure) return "NGUY HIỂM — SMART dự báo ổ sắp hỏng, sao lưu ngay";
            if (Health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase)) return "Không khoẻ — nên thay ổ";
            if (Health.Equals("Warning", StringComparison.OrdinalIgnoreCase)) return "Cảnh báo — theo dõi sát";
            if (Wear >= 90) return "Sắp hết tuổi thọ ghi (wear ≥ 90%)";
            if (Temperature >= 65) return "Nóng bất thường";
            if (Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase)) return "Khoẻ";
            return Health.Length > 0 ? Health : "Không xác định";
        }
    }

    public bool IsSsd => MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase)
                      || BusType.Equals("NVMe", StringComparison.OrdinalIgnoreCase);
}

public sealed class PartitionInfo
{
    public int Disk { get; init; }
    public int Part { get; init; }
    public string Letter { get; init; } = "";
    public long Size { get; init; }
    public long Free { get; init; }
    public long Offset { get; init; }
    public string Type { get; init; } = "";
    public string Label { get; init; } = "";
    public string FileSystem { get; init; } = "";
    public bool IsBoot { get; init; }
    public bool IsSystem { get; init; }
    public bool IsActive { get; init; }
    public long MinSize { get; init; }
    public long MaxSize { get; init; }

    public string LetterText => string.IsNullOrWhiteSpace(Letter) ? "(không có)" : $"{Letter}:";
    public string SizeText => Fmt.Bytes(Size);
    public string FreeText => Free > 0 ? Fmt.Bytes(Free) : "—";
    public string UsedPercentText => Size == 0 || Free == 0 ? "—" : $"{(Size - Free) * 100.0 / Size:0}%";
    public string ResizeRangeText => MaxSize > 0 ? $"{Fmt.Bytes(MinSize)} – {Fmt.Bytes(MaxSize)}" : "—";
    /// <summary>Phân vùng đụng vào là mất máy: boot, system, recovery, EFI.</summary>
    public bool IsProtected => IsBoot || IsSystem
        || Type.Contains("System", StringComparison.OrdinalIgnoreCase)
        || Type.Contains("Recovery", StringComparison.OrdinalIgnoreCase)
        || Type.Contains("Reserved", StringComparison.OrdinalIgnoreCase);
    public string ProtectedText => IsProtected ? "🔒 Được bảo vệ" : "";
    public string Display => $"Disk {Disk} · Phân vùng {Part} · {LetterText} {Label}".TrimEnd();
}

public static class StorageService
{
    // ---------------------------------------------------------------- thông tin & SMART

    public static async Task<List<PhysicalDiskInfo>> GetPhysicalDisksAsync(CancellationToken ct = default)
    {
        const string script = """
            $ErrorActionPreference = 'SilentlyContinue'

            # SMART predict-fail: khớp theo InstanceName chứa 'Disk<N>'
            $predict = @{}
            foreach ($p in (Get-CimInstance -Namespace root\wmi -ClassName MSStorageDriver_FailurePredictStatus)) {
                $predict[$p.InstanceName] = [bool]$p.PredictFailure
            }

            $out = @()
            foreach ($pd in (Get-PhysicalDisk)) {
                $rc = $pd | Get-StorageReliabilityCounter
                $disk = Get-Disk -Number $pd.DeviceId

                $pf = $false
                foreach ($k in $predict.Keys) {
                    if ($k -match "_$($pd.DeviceId)_" -or $k -match "Disk$($pd.DeviceId)") {
                        if ($predict[$k]) { $pf = $true }
                    }
                }

                $out += [pscustomobject]@{
                    Number   = [int]$pd.DeviceId
                    Name     = "$($pd.FriendlyName)"
                    Media    = "$($pd.MediaType)"
                    Bus      = "$($pd.BusType)"
                    Size     = [int64]$pd.Size
                    Health   = "$($pd.HealthStatus)"
                    OpStatus = ($pd.OperationalStatus -join ', ')
                    Serial   = "$($pd.SerialNumber)".Trim()
                    Firmware = "$($pd.FirmwareVersion)"
                    Temp     = $(if ($rc -and $rc.Temperature)   { [int]$rc.Temperature }      else { -1 })
                    Hours    = $(if ($rc -and $rc.PowerOnHours)  { [int64]$rc.PowerOnHours }   else { -1 })
                    Wear     = $(if ($rc -and $rc.Wear -ne $null){ [int]$rc.Wear }             else { -1 })
                    ReadErr  = $(if ($rc) { [int64]$rc.ReadErrorsTotal }  else { -1 })
                    WriteErr = $(if ($rc) { [int64]$rc.WriteErrorsTotal } else { -1 })
                    IsBoot   = $(if ($disk) { [bool]$disk.IsBoot }   else { $false })
                    IsSystem = $(if ($disk) { [bool]$disk.IsSystem } else { $false })
                    Style    = $(if ($disk) { "$($disk.PartitionStyle)" } else { '' })
                    Predict  = $pf
                }
            }
            ConvertTo-Json -InputObject @($out) -Depth 3 -Compress
            """;

        var items = await PowerShellRunner.RunJsonAsync(script, ct).ConfigureAwait(false);
        return items.Select(e => new PhysicalDiskInfo
        {
            Number = (int)PowerShellRunner.Num(e, "Number", 0),
            Name = PowerShellRunner.Str(e, "Name"),
            MediaType = PowerShellRunner.Str(e, "Media"),
            BusType = PowerShellRunner.Str(e, "Bus"),
            Size = PowerShellRunner.Num(e, "Size", 0),
            Health = PowerShellRunner.Str(e, "Health"),
            OperationalStatus = PowerShellRunner.Str(e, "OpStatus"),
            Serial = PowerShellRunner.Str(e, "Serial"),
            Firmware = PowerShellRunner.Str(e, "Firmware"),
            Temperature = (int)PowerShellRunner.Num(e, "Temp"),
            PowerOnHours = PowerShellRunner.Num(e, "Hours"),
            Wear = (int)PowerShellRunner.Num(e, "Wear"),
            ReadErrors = PowerShellRunner.Num(e, "ReadErr"),
            WriteErrors = PowerShellRunner.Num(e, "WriteErr"),
            IsBoot = PowerShellRunner.Bool(e, "IsBoot"),
            IsSystem = PowerShellRunner.Bool(e, "IsSystem"),
            PartitionStyle = PowerShellRunner.Str(e, "Style"),
            PredictFailure = PowerShellRunner.Bool(e, "Predict"),
        }).OrderBy(d => d.Number).ToList();
    }

    public static async Task<List<PartitionInfo>> GetPartitionsAsync(CancellationToken ct = default)
    {
        const string script = """
            $ErrorActionPreference = 'SilentlyContinue'
            $out = @()
            foreach ($p in (Get-Partition)) {
                $vol = Get-Volume -Partition $p
                $sup = Get-PartitionSupportedSize -DiskNumber $p.DiskNumber -PartitionNumber $p.PartitionNumber
                $out += [pscustomobject]@{
                    Disk     = [int]$p.DiskNumber
                    Part     = [int]$p.PartitionNumber
                    Letter   = $(if ($p.DriveLetter -and $p.DriveLetter -ne [char]0) { "$($p.DriveLetter)" } else { '' })
                    Size     = [int64]$p.Size
                    Offset   = [int64]$p.Offset
                    Type     = "$($p.Type)"
                    IsBoot   = [bool]$p.IsBoot
                    IsSystem = [bool]$p.IsSystem
                    IsActive = [bool]$p.IsActive
                    Label    = $(if ($vol) { "$($vol.FileSystemLabel)" } else { '' })
                    Fs       = $(if ($vol) { "$($vol.FileSystem)" }      else { '' })
                    Free     = $(if ($vol) { [int64]$vol.SizeRemaining } else { [int64]0 })
                    MinSize  = $(if ($sup) { [int64]$sup.SizeMin }       else { [int64]0 })
                    MaxSize  = $(if ($sup) { [int64]$sup.SizeMax }       else { [int64]0 })
                }
            }
            ConvertTo-Json -InputObject @($out) -Depth 3 -Compress
            """;

        var items = await PowerShellRunner.RunJsonAsync(script, ct).ConfigureAwait(false);
        return items.Select(e => new PartitionInfo
        {
            Disk = (int)PowerShellRunner.Num(e, "Disk", 0),
            Part = (int)PowerShellRunner.Num(e, "Part", 0),
            Letter = PowerShellRunner.Str(e, "Letter"),
            Size = PowerShellRunner.Num(e, "Size", 0),
            Offset = PowerShellRunner.Num(e, "Offset", 0),
            Type = PowerShellRunner.Str(e, "Type"),
            Label = PowerShellRunner.Str(e, "Label"),
            FileSystem = PowerShellRunner.Str(e, "Fs"),
            IsBoot = PowerShellRunner.Bool(e, "IsBoot"),
            IsSystem = PowerShellRunner.Bool(e, "IsSystem"),
            IsActive = PowerShellRunner.Bool(e, "IsActive"),
            Free = PowerShellRunner.Num(e, "Free", 0),
            MinSize = PowerShellRunner.Num(e, "MinSize", 0),
            MaxSize = PowerShellRunner.Num(e, "MaxSize", 0),
        }).OrderBy(p => p.Disk).ThenBy(p => p.Part).ToList();
    }

    // ---------------------------------------------------------------- bảo trì

    /// <summary>Chọn đúng thao tác theo loại ổ: SSD → ReTrim, HDD → Defrag. Không bao giờ defrag SSD.</summary>
    public static async Task<bool> OptimizeAsync(string driveLetter, bool isSsd, IProgress<string>? progress,
                                                 CancellationToken ct = default)
    {
        var op = isSsd ? "-ReTrim" : "-Defrag";
        var opName = isSsd ? "TRIM" : "chống phân mảnh";
        progress?.Report($"Đang {opName} ổ {driveLetter}: — việc này có thể mất vài phút…");
        Logger.Info($"Bắt đầu {opName} ổ {driveLetter}:");

        var script = $"""
            $ErrorActionPreference = 'Stop'
            try {{
                Optimize-Volume -DriveLetter {driveLetter} {op} -Verbose
                exit 0
            }} catch {{
                Write-Error $_.Exception.Message
                exit 1
            }}
            """;

        var res = await PowerShellRunner.RunAsync(script, ct).ConfigureAwait(false);
        if (res.Ok) { Logger.Success($"Hoàn tất {opName} ổ {driveLetter}:"); return true; }
        Logger.Error($"{opName} ổ {driveLetter}: thất bại — {res.StdErr}");
        return false;
    }

    /// <summary>Quét lỗi ổ đĩa ở chế độ online (không cần khởi động lại, không sửa gì).</summary>
    public static async Task<string> ScanVolumeAsync(string driveLetter, CancellationToken ct = default)
    {
        Logger.Info($"Đang quét lỗi ổ {driveLetter}:");
        var script = $"""
            $ErrorActionPreference = 'Stop'
            try {{
                $r = Repair-Volume -DriveLetter {driveLetter} -Scan
                Write-Output "$r"
                exit 0
            }} catch {{
                Write-Error $_.Exception.Message
                exit 1
            }}
            """;
        var res = await PowerShellRunner.RunAsync(script, ct).ConfigureAwait(false);
        var output = res.Ok ? (res.StdOut.Length > 0 ? res.StdOut : "NoErrorsFound") : res.StdErr;
        Logger.Log(res.Ok ? LogLevel.Success : LogLevel.Error, $"Quét ổ {driveLetter}: → {output}");
        return output;
    }

    // ---------------------------------------------------------------- phân vùng (nguy hiểm)

    public static async Task<bool> ShrinkOrExtendAsync(PartitionInfo p, long newSizeBytes, CancellationToken ct = default)
    {
        Guard(p);
        var script = $"""
            $ErrorActionPreference = 'Stop'
            try {{
                Resize-Partition -DiskNumber {p.Disk} -PartitionNumber {p.Part} -Size {newSizeBytes}
                exit 0
            }} catch {{ Write-Error $_.Exception.Message; exit 1 }}
            """;
        return await RunGuardedAsync(script, $"Đổi kích thước {p.Display} → {Fmt.Bytes(newSizeBytes)}", ct)
            .ConfigureAwait(false);
    }

    public static async Task<bool> DeleteAsync(PartitionInfo p, CancellationToken ct = default)
    {
        Guard(p);
        var script = $"""
            $ErrorActionPreference = 'Stop'
            try {{
                Remove-Partition -DiskNumber {p.Disk} -PartitionNumber {p.Part} -Confirm:$false
                exit 0
            }} catch {{ Write-Error $_.Exception.Message; exit 1 }}
            """;
        return await RunGuardedAsync(script, $"XOÁ {p.Display}", ct).ConfigureAwait(false);
    }

    public static async Task<bool> FormatAsync(PartitionInfo p, string fileSystem, string label, CancellationToken ct = default)
    {
        Guard(p);
        if (string.IsNullOrWhiteSpace(p.Letter))
            throw new InvalidOperationException("Phân vùng chưa có ký tự ổ, không format được.");

        var safeLabel = label.Replace("'", "''");
        var script = $"""
            $ErrorActionPreference = 'Stop'
            try {{
                Format-Volume -DriveLetter {p.Letter} -FileSystem {fileSystem} -NewFileSystemLabel '{safeLabel}' -Force -Confirm:$false
                exit 0
            }} catch {{ Write-Error $_.Exception.Message; exit 1 }}
            """;
        return await RunGuardedAsync(script, $"FORMAT {p.Display} sang {fileSystem}", ct).ConfigureAwait(false);
    }

    public static async Task<bool> CreatePartitionAsync(int diskNumber, long sizeBytes, bool useMax,
                                                        string fileSystem, string label, CancellationToken ct = default)
    {
        var safeLabel = label.Replace("'", "''");
        var sizeArg = useMax ? "-UseMaximumSize" : $"-Size {sizeBytes}";
        var script = $"""
            $ErrorActionPreference = 'Stop'
            try {{
                $disk = Get-Disk -Number {diskNumber}
                if ($disk.IsBoot -or $disk.IsSystem) {{ throw 'Từ chối thao tác trên ổ hệ thống.' }}
                $p = New-Partition -DiskNumber {diskNumber} {sizeArg} -AssignDriveLetter
                Format-Volume -Partition $p -FileSystem {fileSystem} -NewFileSystemLabel '{safeLabel}' -Force -Confirm:$false | Out-Null
                Write-Output "Đã tạo phân vùng $($p.DriveLetter):"
                exit 0
            }} catch {{ Write-Error $_.Exception.Message; exit 1 }}
            """;
        return await RunGuardedAsync(script, $"TẠO phân vùng mới trên Disk {diskNumber}", ct).ConfigureAwait(false);
    }

    public static async Task<bool> AssignLetterAsync(PartitionInfo p, char letter, CancellationToken ct = default)
    {
        var script = $"""
            $ErrorActionPreference = 'Stop'
            try {{
                Set-Partition -DiskNumber {p.Disk} -PartitionNumber {p.Part} -NewDriveLetter {letter}
                exit 0
            }} catch {{ Write-Error $_.Exception.Message; exit 1 }}
            """;
        return await RunGuardedAsync(script, $"Gán ký tự {letter}: cho {p.Display}", ct).ConfigureAwait(false);
    }

    /// <summary>Chặn cứng ở tầng service, không chỉ ở tầng UI.</summary>
    private static void Guard(PartitionInfo p)
    {
        if (p.IsProtected)
            throw new InvalidOperationException(
                $"Từ chối thao tác trên phân vùng được bảo vệ ({p.Display}, loại '{p.Type}'). " +
                "Đây là phân vùng boot / system / recovery — sửa nó sẽ làm máy không khởi động được.");
    }

    private static async Task<bool> RunGuardedAsync(string script, string what, CancellationToken ct)
    {
        Logger.Warn($"Thao tác phân vùng: {what}");
        var res = await PowerShellRunner.RunAsync(script, ct).ConfigureAwait(false);
        if (res.Ok)
        {
            Logger.Success($"Hoàn tất: {what}. {res.StdOut}");
            return true;
        }
        Logger.Error($"Thất bại: {what} — {res.StdErr}");
        return false;
    }
}
