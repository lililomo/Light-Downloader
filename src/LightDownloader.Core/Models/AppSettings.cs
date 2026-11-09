namespace LightDownloader.Core.Models;

public sealed class AppSettings
{
    /// <summary>IDM's default "automatically start downloading" list, plus a few newer formats.</summary>
    public const string DefaultFileTypes =
        "3GP 7Z AAC ACE AIF APK ARJ ASF AVI BIN BZ2 EXE GZ GZIP IMG ISO LZH M4A M4V MKV MOV MP3 MP4 MPA MPE " +
        "MPEG MPG MSI MSU OGG OGV PDF PLJ PPS PPT QT R0* R1* RA RAR RM RMVB SEA SIT SITX TAR TIF TIFF WAV WMA WMV " +
        "Z ZIP WEBM FLAC XZ TGZ ZST DMG DEB RPM MSIX";

    /// <summary>Browsers the integration extension identifies itself as (key → display name).</summary>
    public static readonly IReadOnlyList<(string Key, string Name)> Browsers = new[]
    {
        ("chrome", "Google Chrome"),
        ("firefox", "Mozilla Firefox"),
        ("edge", "Microsoft Edge"),
        ("opera", "Opera"),
        ("brave", "Brave")
    };

    public string DownloadFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>Default number of parallel connections per download.</summary>
    public int DefaultConnections { get; set; } = 8;

    /// <summary>How many downloads may run at the same time; the rest wait in the queue.</summary>
    public int MaxConcurrentDownloads { get; set; } = 3;

    /// <summary>Global speed cap in bytes/second; 0 means unlimited.</summary>
    public long SpeedLimitBytesPerSec { get; set; } = 0;

    /// <summary>Preferred loopback port for the browser bridge.</summary>
    public int ServerPort { get; set; } = 7414;

    public bool StartMinimized { get; set; } = false;

    /// <summary>Start with Windows (minimized to the tray) so browser downloads are always captured.</summary>
    public bool RunAtStartup { get; set; } = true;

    /// <summary>Look for a new version at startup (and twice a day while running).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>A version the user chose to skip; its prompt isn't shown again.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>Show the "New download" dialog (pick the folder) before a captured download starts.</summary>
    public bool AskWhereToSave { get; set; } = true;

    /// <summary>Open the per-download progress window when a download is started.</summary>
    public bool ShowProgressWindow { get; set; } = true;

    /// <summary>Folders remembered per category, e.g. "Video" → "D:\Videos".</summary>
    public Dictionary<string, string> CategoryFolders { get; set; } = new();

    // Browser integration.
    /// <summary>Master switch: take over browser downloads at all.</summary>
    public bool BrowserCapture { get; set; } = true;

    /// <summary>Browser keys (see <see cref="Browsers"/>) whose downloads are left to the browser.</summary>
    public List<string> DisabledBrowsers { get; set; } = new();

    /// <summary>Space-separated extensions to capture; "*" is a wildcard (R0* = .r00, .r01…).</summary>
    public string CaptureFileTypes { get; set; } = DefaultFileTypes;

    /// <summary>Space-separated hosts never captured; "*.example.com" covers subdomains.</summary>
    public string SkipSites { get; set; } = "";

    /// <summary>Show the "Download this video" button over web video players.</summary>
    public bool VideoButton { get; set; } = true;

    // Simple daily scheduler.
    public bool SchedulerEnabled { get; set; } = false;
    public string? StartTime { get; set; } // "HH:mm" — start all downloads
    public string? StopTime { get; set; }  // "HH:mm" — pause all downloads

    public bool CapturesBrowser(string? browser)
        => BrowserCapture && !DisabledBrowsers.Contains(browser ?? "", StringComparer.OrdinalIgnoreCase);

    public static string[] SplitList(string? text)
        => (text ?? "").Split(new[] { ' ', ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    public AppSettings Clone()
    {
        var c = (AppSettings)MemberwiseClone();
        c.CategoryFolders = new Dictionary<string, string>(CategoryFolders);
        c.DisabledBrowsers = new List<string>(DisabledBrowsers);
        return c;
    }
}
