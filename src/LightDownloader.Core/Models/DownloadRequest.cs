namespace LightDownloader.Core.Models;

/// <summary>Payload sent by the browser extension (or the Add-URL dialog) to queue a download.</summary>
public sealed class DownloadRequest
{
    public string Url { get; set; } = "";
    public string? FileName { get; set; }
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public string? Cookies { get; set; }
    public long FileSize { get; set; } = -1;
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>"http" (default) or "video" (grab with yt-dlp).</summary>
    public string? Kind { get; set; }

    /// <summary>Video quality for yt-dlp: "best", a max height such as "720", "m4a" or "mp3".</summary>
    public string? Format { get; set; }

    /// <summary>Video title from the page, shown until yt-dlp names the file.</summary>
    public string? Title { get; set; }

    /// <summary>Browser that sent the request ("chrome", "firefox", …).</summary>
    public string? Browser { get; set; }

    /// <summary>Target folder; when empty the remembered folder for the file's category is used.</summary>
    public string? SaveFolder { get; set; }
}
