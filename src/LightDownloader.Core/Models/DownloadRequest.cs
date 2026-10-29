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
}
