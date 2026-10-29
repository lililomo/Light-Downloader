namespace LightDownloader.Core.Models;

public enum DownloadStatus
{
    Queued,
    Connecting,
    Downloading,
    Paused,
    Completed,
    Error
}
