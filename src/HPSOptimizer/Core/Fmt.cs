namespace HPSOptimizer.Core;

public static class Fmt
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Bytes(long bytes)
    {
        if (bytes < 0) return "—";
        if (bytes == 0) return "0 B";
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < Units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{v:0} {Units[i]}" : $"{v:0.##} {Units[i]}";
    }

    public static string Hours(long h) => h < 0 ? "—" : $"{h:N0} giờ (~{h / 24:N0} ngày)";
    public static string Percent(double p) => $"{p:0.#}%";
    public static string Temp(int c) => c <= 0 ? "—" : $"{c} °C";
}
