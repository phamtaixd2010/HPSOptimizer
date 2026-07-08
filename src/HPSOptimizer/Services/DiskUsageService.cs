using System.IO;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using HPSOptimizer.Core;

namespace HPSOptimizer.Services;

public sealed class UsageNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsFile { get; init; }
    public long Size { get; set; }
    public int FileCount { get; set; }
    public ObservableCollection<UsageNode> Children { get; } = new();

    /// <summary>Phần trăm so với thư mục gốc đã quét, dùng để vẽ thanh ngang.</summary>
    public double SharePercent { get; set; }

    public string SizeText => Fmt.Bytes(Size);
    public string Header => IsFile
        ? $"{Name}   —   {SizeText}"
        : $"{Name}   —   {SizeText}  ({FileCount:N0} file)";
    public double BarWidth => Math.Max(2, SharePercent * 2.4); // 0..240 px
}

public sealed record DuplicateGroup(long Size, List<string> Files)
{
    public string Header => $"{Files.Count} bản sao × {Fmt.Bytes(Size)}  →  lãng phí {Fmt.Bytes(Size * (Files.Count - 1))}";
    public long Wasted => Size * (Files.Count - 1);
}

public static class DiskUsageService
{
    /// <summary>Quét cây thư mục. Trả node gốc; con được sắp xếp giảm dần theo dung lượng.</summary>
    public static Task<UsageNode> ScanAsync(string root, int maxDepth, IProgress<string>? progress,
                                            CancellationToken ct = default)
        => Task.Run(() =>
        {
            var node = ScanDir(root, 0, maxDepth, progress, ct);
            AssignShare(node, node.Size);
            return node;
        }, ct);

    private static UsageNode ScanDir(string path, int depth, int maxDepth, IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var node = new UsageNode
        {
            Name = depth == 0 ? path : Path.GetFileName(path),
            FullPath = path,
            IsFile = false
        };

        if (depth <= 1) progress?.Report($"Đang quét {path}…");

        // Kích thước file ngay trong thư mục này.
        long ownFiles = 0;
        var bigFiles = new List<UsageNode>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(path))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var len = new FileInfo(f).Length;
                    ownFiles += len;
                    node.FileCount++;
                    if (depth < maxDepth && len >= 50L * 1024 * 1024) // chỉ hiện file ≥ 50MB cho gọn
                        bigFiles.Add(new UsageNode { Name = Path.GetFileName(f), FullPath = f, IsFile = true, Size = len });
                }
                catch { /* file khoá hoặc vừa bị xoá */ }
            }
        }
        catch (UnauthorizedAccessException) { /* thư mục hệ thống */ }
        catch (IOException) { }

        var childDirs = new List<UsageNode>();
        try
        {
            foreach (var d in Directory.EnumerateDirectories(path))
            {
                ct.ThrowIfCancellationRequested();
                // Bỏ qua reparse point để không đi vòng vô hạn qua junction/symlink.
                try
                {
                    var attrs = File.GetAttributes(d);
                    if (attrs.HasFlag(FileAttributes.ReparsePoint)) continue;
                }
                catch { continue; }

                var child = ScanDir(d, depth + 1, maxDepth, progress, ct);
                childDirs.Add(child);
                node.FileCount += child.FileCount;
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        node.Size = ownFiles + childDirs.Sum(c => c.Size);

        if (depth < maxDepth)
        {
            foreach (var c in childDirs.Concat(bigFiles).OrderByDescending(c => c.Size).Take(40))
                node.Children.Add(c);
        }

        return node;
    }

    private static void AssignShare(UsageNode node, long rootSize)
    {
        node.SharePercent = rootSize == 0 ? 0 : node.Size * 100.0 / rootSize;
        foreach (var c in node.Children) AssignShare(c, rootSize);
    }

    // ---------------------------------------------------------------- file trùng lặp

    /// <summary>
    /// Tìm file trùng: nhóm theo kích thước → hash 64KB đầu → hash toàn bộ.
    /// Ba tầng để không phải đọc hết đĩa.
    /// </summary>
    public static Task<List<DuplicateGroup>> FindDuplicatesAsync(string root, long minSize,
        IProgress<string>? progress, CancellationToken ct = default) => Task.Run(() =>
    {
        progress?.Report("Đang liệt kê file…");
        var bySize = new Dictionary<long, List<string>>();

        foreach (var f in EnumerateFilesSafe(root, ct))
        {
            try
            {
                var len = new FileInfo(f).Length;
                if (len < minSize) continue;
                if (!bySize.TryGetValue(len, out var list)) bySize[len] = list = new List<string>();
                list.Add(f);
            }
            catch { }
        }

        var candidates = bySize.Where(kv => kv.Value.Count > 1).ToList();
        progress?.Report($"{candidates.Count} nhóm cùng kích thước, đang so sánh nội dung…");

        var groups = new List<DuplicateGroup>();
        var done = 0;

        foreach (var (size, files) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            if (done % 25 == 0) progress?.Report($"So sánh {done}/{candidates.Count} nhóm…");

            // Tầng 2: hash 64KB đầu.
            var byHead = files.GroupBy(f => HashPartial(f, 64 * 1024))
                              .Where(g => g.Key is not null && g.Count() > 1);

            foreach (var headGroup in byHead)
            {
                // Tầng 3: hash toàn bộ.
                var byFull = headGroup.GroupBy(f => HashPartial(f, long.MaxValue))
                                      .Where(g => g.Key is not null && g.Count() > 1);
                foreach (var full in byFull)
                    groups.Add(new DuplicateGroup(size, full.ToList()));
            }
        }

        return groups.OrderByDescending(g => g.Wasted).Take(200).ToList();
    }, ct);

    private static string? HashPartial(string path, long maxBytes)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();

            if (maxBytes == long.MaxValue)
                return Convert.ToHexString(sha.ComputeHash(fs));

            var buffer = new byte[Math.Min(maxBytes, fs.Length)];
            var read = fs.Read(buffer, 0, buffer.Length);
            return Convert.ToHexString(sha.ComputeHash(buffer, 0, read));
        }
        catch
        {
            return null; // file khoá → coi như không so được, tự loại khỏi nhóm
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            string[] files;
            try { files = Directory.GetFiles(dir); } catch { continue; }
            foreach (var f in files) yield return f;

            try
            {
                foreach (var d in Directory.GetDirectories(dir))
                {
                    try { if (File.GetAttributes(d).HasFlag(FileAttributes.ReparsePoint)) continue; } catch { continue; }
                    stack.Push(d);
                }
            }
            catch { }
        }
    }
}
