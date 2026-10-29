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
    public DateTime? LastTryAt { get; set; }

    /// <summary>Per-download speed cap in bytes/second (on top of the global limit); 0 = none.</summary>
    public long SpeedLimitBytesPerSec { get; set; }

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
        _status == DownloadStatus.Completed ? 1 // video/convert items keep no byte counts after a restart
        : PercentOverride is double pct ? Math.Clamp(pct / 100.0, 0, 1)
        : TotalSize > 0 ? Math.Clamp((double)Downloaded / TotalSize, 0, 1)
        : 0;

    [JsonIgnore] public double ProgressPercent => Progress * 100.0;

    [JsonIgnore]
    public string SizeText =>
        Kind != DownloadKind.Http
            ? (TotalSize > 0 ? Formatting.Bytes(TotalSize) : "—")
            : TotalSize > 0
                ? $"{Formatting.Bytes(Downloaded)} / {Formatting.Bytes(TotalSize)}"
                : Downloaded > 0 ? Formatting.Bytes(Downloaded) : "—";

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

    [JsonIgnore]
    public string Category => Kind == DownloadKind.Video ? Categories.Video : Categories.FromFileName(FileName);

    [JsonIgnore]
    public bool IsActive => _status is DownloadStatus.Downloading or DownloadStatus.Connecting;

    [JsonIgnore]
    public string Host
    {
        get
        {
            if (Kind == DownloadKind.Convert) return "Local file";
            return Uri.TryCreate(Url, UriKind.Absolute, out var u) ? u.Host : "";
        }
    }

    [JsonIgnore] public string SubLine => $"{Host} · {AddedAt:dd MMM yyyy, HH:mm}";

    /// <summary>Short status shown under the progress bar in the list.</summary>
    [JsonIgnore]
    public string StatusLine
    {
        get
        {
            bool known = TotalSize > 0 || PercentOverride.HasValue;
            return _status switch
            {
                DownloadStatus.Downloading when Kind == DownloadKind.Video && PercentOverride is null => "Fetching video info…",
                DownloadStatus.Downloading when Kind == DownloadKind.Convert => $"Converting · {ProgressPercent:0.#}%",
                DownloadStatus.Downloading => known ? $"Downloading · {ProgressPercent:0.#}%" : $"Downloading · {Formatting.Bytes(Downloaded)}",
                DownloadStatus.Connecting => "Connecting…",
                DownloadStatus.Paused => known ? $"Paused · {ProgressPercent:0.#}%" : "Paused",
                DownloadStatus.Queued => "Queued",
                DownloadStatus.Completed => "Completed",
                DownloadStatus.Error => "Error: " + Error,
                _ => _status.ToString()
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly string[] NotifyProps =
    {
        nameof(Status), nameof(StatusText), nameof(StatusLine), nameof(Downloaded), nameof(Progress),
        nameof(ProgressPercent), nameof(SizeText), nameof(SpeedText), nameof(EtaText),
        nameof(TotalSize), nameof(FileName), nameof(Category), nameof(IsActive), nameof(Host),
        nameof(SubLine), nameof(SaveFolder), nameof(SpeedLimitBytesPerSec)
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
