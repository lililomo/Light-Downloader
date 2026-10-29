using System.Diagnostics;
using System.IO;

namespace LightDownloader.App;

/// <summary>Opens files and folders with Windows Explorer.</summary>
internal static class Shell
{
    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* no associated app, or the file is gone */ }
    }

    /// <summary>Shows the file selected in Explorer, or opens <paramref name="fallbackFolder"/> if it doesn't exist.</summary>
    public static void Reveal(string? path, string fallbackFolder)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
            {
                var folder = !string.IsNullOrEmpty(path) && Directory.Exists(Path.GetDirectoryName(path))
                    ? Path.GetDirectoryName(path)!
                    : fallbackFolder;
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
        }
        catch { /* ignore */ }
    }
}
