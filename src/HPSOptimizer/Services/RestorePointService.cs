using HPSOptimizer.Core;

namespace HPSOptimizer.Services;

/// <summary>
/// Tạo System Restore Point trước khi thay đổi hệ thống.
/// Windows mặc định chặn tạo nhiều hơn 1 điểm / 24h — ta set SystemRestorePointCreationFrequency=0
/// (khoá registry chính thức của Microsoft) để mỗi lần tối ưu đều có điểm khôi phục riêng.
/// </summary>
public static class RestorePointService
{
    /// <summary>Chỉ tạo tối đa 1 restore point mỗi phiên chạy app, tránh spam.</summary>
    private static bool _createdThisSession;

    public static async Task<bool> EnsureAsync(string description, CancellationToken ct = default)
    {
        if (_createdThisSession)
        {
            Logger.Info("Đã có điểm khôi phục trong phiên này, bỏ qua.");
            return true;
        }
        var ok = await CreateAsync(description, ct).ConfigureAwait(false);
        if (ok) _createdThisSession = true;
        return ok;
    }

    public static async Task<bool> CreateAsync(string description, CancellationToken ct = default)
    {
        Logger.Info($"Đang tạo điểm khôi phục: {description}");

        var safe = description.Replace("'", "''");
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                $key = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
                if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
                New-ItemProperty -Path $key -Name 'SystemRestorePointCreationFrequency' -Value 0 -PropertyType DWord -Force | Out-Null

                Enable-ComputerRestore -Drive "$env:SystemDrive\"
                Checkpoint-Computer -Description '{{safe}}' -RestorePointType 'MODIFY_SETTINGS'
                Write-Output 'CREATED'
                exit 0
            } catch {
                Write-Error $_.Exception.Message
                exit 1
            }
            """;

        var res = await PowerShellRunner.RunAsync(script, ct).ConfigureAwait(false);
        if (res.Ok && res.StdOut.Contains("CREATED"))
        {
            Logger.Success("Đã tạo điểm khôi phục hệ thống.");
            return true;
        }

        Logger.Warn("Không tạo được điểm khôi phục. Nguyên nhân thường gặp: System Protection đang tắt, " +
                    "ổ C: hết dung lượng shadow copy, hoặc bản Windows Home bị giới hạn. " +
                    $"Chi tiết: {res.StdErr}");
        return false;
    }

    /// <summary>Mở giao diện rstrui.exe để người dùng khôi phục thủ công.</summary>
    public static void OpenRestoreUi()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rstrui.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error("Mở System Restore", ex);
        }
    }
}
