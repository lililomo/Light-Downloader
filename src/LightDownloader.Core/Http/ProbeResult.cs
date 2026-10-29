namespace LightDownloader.Core.Http;

public sealed class ProbeResult
{
    public string FinalUrl { get; set; } = "";
    public string? FileName { get; set; }
    public long TotalSize { get; set; } = -1;
    public bool SupportsRanges { get; set; }
    public string? ContentType { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
}
