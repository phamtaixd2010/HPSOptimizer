using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HPSOptimizer.Core;

namespace HPSOptimizer.Services;

public sealed record PsResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Chạy script PowerShell qua -EncodedCommand để tránh mọi vấn đề escape dấu nháy.
/// Dùng cho các tác vụ Storage/Restore Point vì cmdlet chuẩn của Windows đáng tin hơn tự gọi WMI.
/// </summary>
public static class PowerShellRunner
{
    public static async Task<PsResult> RunAsync(string script, CancellationToken ct = default)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var proc = new Process { StartInfo = psi };
        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { /* ignore */ }
            throw;
        }

        var result = new PsResult(proc.ExitCode, sbOut.ToString().Trim(), sbErr.ToString().Trim());
        if (!result.Ok && result.StdErr.Length > 0)
            Logger.Warn($"PowerShell exit {result.ExitCode}: {Truncate(result.StdErr, 300)}");
        return result;
    }

    /// <summary>Chạy script trả JSON và parse thành mảng JsonElement. Trả mảng rỗng nếu lỗi.</summary>
    public static async Task<List<JsonElement>> RunJsonAsync(string script, CancellationToken ct = default)
    {
        var res = await RunAsync(script, ct).ConfigureAwait(false);
        var list = new List<JsonElement>();
        if (string.IsNullOrWhiteSpace(res.StdOut)) return list;

        try
        {
            using var doc = JsonDocument.Parse(res.StdOut);
            var root = doc.RootElement.Clone();
            if (root.ValueKind == JsonValueKind.Array)
                list.AddRange(root.EnumerateArray());
            else if (root.ValueKind == JsonValueKind.Object)
                list.Add(root);
        }
        catch (JsonException ex)
        {
            Logger.Warn($"Không parse được JSON từ PowerShell: {ex.Message}");
        }
        return list;
    }

    // ---- Helper đọc field JSON an toàn (PowerShell có thể trả null hoặc string) ----

    public static string Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.ToString(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => ""
        };
    }

    public static long Num(JsonElement e, string name, long fallback = -1)
    {
        if (!e.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        return fallback;
    }

    public static bool Bool(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
            _ => false
        };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
