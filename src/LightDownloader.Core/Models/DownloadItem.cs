using System.ComponentModel;
using System.Text.Json.Serialization;
using LightDownloader.Core.Util;

namespace LightDownloader.Core.Models;

public sealed class DownloadItem : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = "";
    public string? FinalUrl { get; set; }
    public string FileName { get; set; } = "download";
    public string SaveFolder { get; set; } = "";
    public long TotalSize { get; set; } = -1;
    public int Connections { get; set; } = 8;
    public bool SupportsRanges { get; set; }
    public DownloadKind Kind { get; set; } = DownloadKind.Http;
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public string? Cookies { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();

    public List<SegmentState> Segments { get; set; } = new();

    // Validation tokens so a resume can detect that the remote file changed.
    public string? ETag { get; set; }
    public string? LastModified { get; set; }

    private DownloadStatus _status = DownloadStatus.Queued;
    public DownloadStatus Status
    {
        get => _status;
        set => _status = value; // change notification is raised via Notify() on the UI thread
    }

    public string? Error { get; set; }

    [JsonIgnore] public string FullPath => Path.Combine(SaveFolder, FileName);
    [JsonIgnore] public string ControlPath => FullPath + ".ldm";

    [JsonIgnore]
    public long Downloaded
    {
        get
        {
            long s = 0;
            var segs = Segments;
            for (int i = 0; i < segs.Count; i++) s += segs[i].Downloaded;
            return s;
        }
    }

    [JsonIgnore] public double SpeedBytesPerSec { get; set; }

    /// <summary>For Video/Convert tasks that report a percentage instead of byte counts.</summary>
    [JsonIgnore] public double? PercentOverride { get; set; }

    [JsonIgnore]
    public double Progress =>
        PercentOverride is double pct ? Math.Clamp(pct / 100.0, 0, 1)
        : TotalSize > 0 ? Math.Clamp((double)Downloaded / TotalSize, 0, 1)
        : 0;

    [JsonIgnore] public double ProgressPercent => Progress * 100.0;

    [JsonIgnore]
    public string SizeText =>
        Kind != DownloadKind.Http
            ? (TotalSize > 0 ? Formatting.Bytes(TotalSize) : "—")
            : TotalSize > 0
                ? $"{Formatting.Bytes(Downloaded)} / {Formatting.Bytes(TotalSize)}"
                : Formatting.Bytes(Downloaded);

    [JsonIgnore]
    public string SpeedText => _status == DownloadStatus.Downloading ? Formatting.Speed(SpeedBytesPerSec) : "—";

    [JsonIgnore]
    public string EtaText
    {
        get
        {
            if (Kind != DownloadKind.Http) return "—";
            if (_status != DownloadStatus.Downloading || SpeedBytesPerSec <= 1 || TotalSize <= 0) return "—";
            long remaining = TotalSize - Downloaded;
            return Formatting.Duration(TimeSpan.FromSeconds(remaining / SpeedBytesPerSec));
        }
    }

    [JsonIgnore]
    public string StatusText => _status switch
    {
        DownloadStatus.Error => $"Error: {Error}",
        _ => _status.ToString()
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly string[] NotifyProps =
    {
        nameof(Status), nameof(StatusText), nameof(Downloaded), nameof(Progress),
        nameof(ProgressPercent), nameof(SizeText), nameof(SpeedText), nameof(EtaText),
        nameof(TotalSize), nameof(FileName)
    };

    /// <summary>Raise change notifications for all display properties. Call on the UI thread.</summary>
    public void Notify()
    {
        var h = PropertyChanged;
        if (h == null) return;
        foreach (var p in NotifyProps)
            h(this, new PropertyChangedEventArgs(p));
    }
}
