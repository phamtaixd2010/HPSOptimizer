using System.Runtime.InteropServices;
using HPSOptimizer.Core;

namespace HPSOptimizer.Services;

public sealed class CleanTarget : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    /// <summary>Danh sách thư mục quét. Rỗng nếu là mục đặc biệt (Recycle Bin).</summary>
    public List<string> Folders { get; init; } = new();
    public string Pattern { get; init; } = "*";
    /// <summary>Đề xuất tick sẵn khi mở app.</summary>
    public bool DefaultOn { get; init; } = true;
    public bool IsRecycleBin { get; init; }
    /// <summary>Cảnh báo hiển thị màu cam.</summary>
    public string? Warning { get; init; }

    private bool _selected;
    public bool Selected { get => _selected; set => Set(ref _selected, value); }

    private long _size = -1;
    public long Size { get => _size; set { if (Set(ref _size, value)) OnPropertyChanged(nameof(SizeText)); } }
    public string SizeText => Size < 0 ? "chưa quét" : Fmt.Bytes(Size);

    private int _fileCount;
    public int FileCount { get => _fileCount; set => Set(ref _fileCount, value); }
}

public sealed record CleanResult(long BytesFreed, int FilesDeleted, int FilesSkipped);

public static class CleanerService
{
    private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string SystemDrive => Path.GetPathRoot(Win) ?? @"C:\";

    public static List<CleanTarget> BuildTargets()
    {
        var targets = new List<CleanTarget>
        {
            new()
            {
                Id = "usertemp", Name = "Thư mục tạm của người dùng",
                Description = "%TEMP% — nơi phần mềm ném file rác khi cài đặt và giải nén.",
                Folders = { Path.GetTempPath() }
            },
            new()
            {
                Id = "wintemp", Name = "Thư mục tạm của Windows",
                Description = @"C:\Windows\Temp — an toàn để xoá; file đang khoá sẽ tự động bỏ qua.",
                Folders = { Path.Combine(Win, "Temp") }
            },
            new()
            {
                Id = "prefetch", Name = "Prefetch",
                Description = "Windows sẽ tự dựng lại. Chỉ có ích khi cần reset thói quen nạp app.",
                Folders = { Path.Combine(Win, "Prefetch") }, Pattern = "*.pf",
                DefaultOn = false
            },
            new()
            {
                Id = "wucache", Name = "Bộ nhớ đệm Windows Update",
                Description = "Các gói cập nhật đã cài xong nhưng còn để lại. Thường vài GB.",
                Folders = { Path.Combine(Win, "SoftwareDistribution", "Download") }
            },
            new()
            {
                Id = "deliveryopt", Name = "Delivery Optimization",
                Description = "Bộ đệm chia sẻ bản cập nhật cho máy khác trong mạng LAN.",
                Folders = { Path.Combine(Win, "SoftwareDistribution", "DeliveryOptimization") }
            },
            new()
            {
                Id = "logs", Name = "Nhật ký hệ thống (CBS, DISM)",
                Description = @"C:\Windows\Logs — chỉ hữu ích khi đang gỡ lỗi cài đặt Windows.",
                Folders = { Path.Combine(Win, "Logs") }, Pattern = "*.log",
                DefaultOn = false
            },
            new()
            {
                Id = "dumps", Name = "File dump khi ứng dụng crash",
                Description = "Minidump và CrashDumps. Vô dụng nếu bạn không phân tích crash.",
                Folders =
                {
                    Path.Combine(Win, "Minidump"),
                    Path.Combine(LocalAppData, "CrashDumps")
                },
                DefaultOn = false
            },
            new()
            {
                Id = "thumbs", Name = "Bộ nhớ đệm ảnh thu nhỏ",
                Description = "thumbcache_*.db. Explorer sẽ dựng lại, lần đầu mở thư mục ảnh sẽ hơi chậm.",
                Folders = { Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer") },
                Pattern = "thumbcache_*.db", DefaultOn = false
            },
            new()
            {
                Id = "recyclebin", Name = "Thùng rác",
                Description = "Xoá vĩnh viễn mọi thứ trong Recycle Bin của tất cả các ổ.",
                IsRecycleBin = true, DefaultOn = false,
                Warning = "Không khôi phục lại được."
            },
            new()
            {
                Id = "windowsold", Name = "Windows.old (bản Windows cũ)",
                Description = "Còn lại sau khi nâng cấp Windows. Thường 10–25 GB. " +
                              "Phần lớn file thuộc quyền TrustedInstaller nên app sẽ bỏ qua chúng — " +
                              "muốn xoá sạch hãy dùng Disk Cleanup > Clean up system files.",
                Folders = { Path.Combine(SystemDrive, "Windows.old") },
                DefaultOn = false,
                Warning = "Xoá xong sẽ KHÔNG thể quay về bản Windows trước đó."
            },
        };

        // Cache trình duyệt — chỉ thêm nếu thư mục tồn tại.
        var browser = new CleanTarget
        {
            Id = "browsers", Name = "Bộ nhớ đệm trình duyệt",
            Description = "Chrome, Edge, Firefox, Cốc Cốc. Không đụng vào mật khẩu, bookmark hay cookie đăng nhập.",
            DefaultOn = false
        };
        foreach (var f in BrowserCacheFolders().Where(Directory.Exists))
            browser.Folders.Add(f);
        if (browser.Folders.Count > 0) targets.Add(browser);

        foreach (var t in targets) t.Selected = t.DefaultOn;
        return targets;
    }

    private static IEnumerable<string> BrowserCacheFolders()
    {
        var lad = LocalAppData;
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        yield return Path.Combine(lad, @"Google\Chrome\User Data\Default\Cache");
        yield return Path.Combine(lad, @"Google\Chrome\User Data\Default\Code Cache");
        yield return Path.Combine(lad, @"Microsoft\Edge\User Data\Default\Cache");
        yield return Path.Combine(lad, @"Microsoft\Edge\User Data\Default\Code Cache");
        yield return Path.Combine(lad, @"CocCoc\Browser\User Data\Default\Cache");

        var ffRoot = Path.Combine(lad, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(ffRoot))
            foreach (var profile in Directory.EnumerateDirectories(ffRoot))
                yield return Path.Combine(profile, "cache2");

        _ = roaming; // giữ lại cho các trình duyệt lưu cache ở Roaming trong tương lai
    }

    // ---------------------------------------------------------------- quét

    public static Task ScanAsync(CleanTarget target, CancellationToken ct = default) => Task.Run(() =>
    {
        if (target.IsRecycleBin)
        {
            var (size, count) = QueryRecycleBin();
            target.Size = size;
            target.FileCount = (int)count;
            return;
        }

        long total = 0;
        int files = 0;
        foreach (var folder in target.Folders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in SafeEnumerateFiles(folder, target.Pattern, ct))
            {
                try
                {
                    total += new FileInfo(file).Length;
                    files++;
                }
                catch { /* file biến mất giữa chừng */ }
            }
        }
        target.Size = total;
        target.FileCount = files;
    }, ct);

    // ---------------------------------------------------------------- dọn

    public static Task<CleanResult> CleanAsync(CleanTarget target, IProgress<string>? progress = null,
                                               CancellationToken ct = default) => Task.Run(() =>
    {
        if (target.IsRecycleBin)
        {
            var (sizeBefore, _) = QueryRecycleBin();
            const uint noConfirm = 0x1, noProgress = 0x2, noSound = 0x4;
            var hr = SHEmptyRecycleBin(IntPtr.Zero, null, noConfirm | noProgress | noSound);
            // 0 = S_OK, -2147418113 (E_UNEXPECTED) xảy ra khi thùng rác vốn đã rỗng.
            if (hr != 0 && sizeBefore > 0)
                Logger.Warn($"Dọn thùng rác trả mã 0x{hr:X8}.");
            target.Size = 0;
            return new CleanResult(sizeBefore, target.FileCount, 0);
        }

        long freed = 0;
        int deleted = 0, skipped = 0;

        foreach (var folder in target.Folders)
        {
            if (!Directory.Exists(folder)) continue;
            progress?.Report($"Đang dọn {folder}…");

            foreach (var file in SafeEnumerateFiles(folder, target.Pattern, ct))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var length = new FileInfo(file).Length;
                    File.SetAttributes(file, FileAttributes.Normal); // gỡ ReadOnly
                    File.Delete(file);
                    freed += length;
                    deleted++;
                }
                catch
                {
                    // File đang bị khoá bởi tiến trình khác — bỏ qua, đây là chuyện bình thường.
                    skipped++;
                }
            }

            // Dọn thư mục con rỗng còn sót lại (không xoá chính thư mục gốc).
            if (target.Pattern == "*")
                foreach (var dir in SafeEnumerateDirectories(folder, ct))
                    try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
                    catch { /* bỏ qua */ }
        }

        target.Size = 0;
        Logger.Success($"{target.Name}: giải phóng {Fmt.Bytes(freed)} ({deleted} file, bỏ qua {skipped} file đang khoá).");
        return new CleanResult(freed, deleted, skipped);
    }, ct);

    // ---------------------------------------------------------------- tiện ích

    /// <summary>Duyệt file đệ quy, nuốt UnauthorizedAccess/PathTooLong thay vì ném ra giữa chừng.</summary>
    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            string[] files;
            try { files = Directory.GetFiles(dir, pattern); }
            catch { continue; }

            foreach (var f in files) yield return f;

            try { foreach (var sub in Directory.GetDirectories(dir)) stack.Push(sub); }
            catch { /* không đọc được thư mục con */ }
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root, CancellationToken ct)
    {
        var all = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            try
            {
                foreach (var sub in Directory.GetDirectories(dir)) { all.Add(sub); stack.Push(sub); }
            }
            catch { /* bỏ qua */ }
        }
        // Xoá từ sâu ra nông.
        all.Reverse();
        return all;
    }

    private static (long Size, long Count) QueryRecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        var hr = SHQueryRecycleBin(null, ref info);
        return hr == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}
