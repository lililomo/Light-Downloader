namespace LightDownloader.Core.Models;

public enum DownloadKind
{
    /// <summary>Plain HTTP(S) file download via the segmented engine.</summary>
    Http,
    /// <summary>Video grabbed and merged with yt-dlp + ffmpeg.</summary>
    Video,
    /// <summary>Local media converted/remuxed with ffmpeg.</summary>
    Convert
}
