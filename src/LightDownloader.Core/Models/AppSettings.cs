namespace LightDownloader.Core.Models;

public sealed class AppSettings
{
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

    /// <summary>Show the "New download" dialog (pick the folder) before a captured download starts.</summary>
    public bool AskWhereToSave { get; set; } = true;

    /// <summary>Open the per-download progress window when a download is started.</summary>
    public bool ShowProgressWindow { get; set; } = true;

    /// <summary>Folders remembered per category, e.g. "Video" → "D:\Videos".</summary>
    public Dictionary<string, string> CategoryFolders { get; set; } = new();

    // Simple daily scheduler.
    public bool SchedulerEnabled { get; set; } = false;
    public string? StartTime { get; set; } // "HH:mm" — start all downloads
    public string? StopTime { get; set; }  // "HH:mm" — pause all downloads

    public AppSettings Clone()
    {
        var c = (AppSettings)MemberwiseClone();
        c.CategoryFolders = new Dictionary<string, string>(CategoryFolders);
        return c;
    }
}
