namespace LightDownloader.Core.Util;

public static class Formatting
{
    public static string Bytes(long b)
    {
        if (b < 0) return "—";
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{v:0} {u[i]}" : $"{v:0.##} {u[i]}";
    }

    public static string Speed(double bytesPerSec)
        => bytesPerSec <= 0 ? "—" : Bytes((long)bytesPerSec) + "/s";

    public static string Duration(TimeSpan t)
    {
        if (t.TotalSeconds < 0 || double.IsInfinity(t.TotalSeconds) || double.IsNaN(t.TotalSeconds))
            return "—";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }
}
