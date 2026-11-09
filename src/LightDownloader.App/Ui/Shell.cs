using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LightDownloader.App;

/// <summary>Opens files, folders and web pages with Windows.</summary>
internal static class Shell
{
    public const string ChromeExtensionUrl = "https://chromewebstore.google.com/detail/light-downloader-integrat/oaghadmejhjmmkagkdimogebdncmmpci";
    public const string FirefoxAddonUrl = "https://addons.mozilla.org/en-US/firefox/addon/light-downloader-integration/";
    public const string WebsiteUrl = "https://beliaja.id/light-downloader/";

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* no associated app, or the file is gone */ }
    }

    /// <summary>Opens a page in a specific browser ("chrome.exe", "firefox.exe"), or the default one if it isn't installed.</summary>
    public static void OpenIn(string browserExe, string url)
    {
        var exe = BrowserPath(browserExe);
        if (exe == null) { Open(url); return; }
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { url } }); }
        catch { Open(url); }
    }

    public static string? BrowserPath(string browserExe)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + browserExe);
            if (key?.GetValue(null) is string p && File.Exists(p.Trim('"'))) return p.Trim('"');
        }
        return null;
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
