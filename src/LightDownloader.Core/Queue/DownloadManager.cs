using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LightDownloader.Core.Convert;
using LightDownloader.Core.Engine;
using LightDownloader.Core.Http;
using LightDownloader.Core.Models;
using LightDownloader.Core.Tools;
using LightDownloader.Core.Video;

namespace LightDownloader.Core.Queue;

/// <summary>Owns the download list, the queue scheduler, persistence and the shared HTTP client.</summary>
public sealed class DownloadManager : IDisposable
{
    private readonly HttpClient _http;
    private readonly RateLimiter _rate;
    private readonly DownloadEngine _engine;
    private readonly YtDlpRunner _video;
    private readonly FfmpegConverter _converter;
    private readonly string _dataDir;
    private readonly object _gate = new();

    private readonly List<DownloadItem> _items = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();
    private readonly Dictionary<Guid, long> _lastBytes = new();
    private readonly System.Threading.Timer _speedTimer;
    private DateTime _lastSpeedTick = DateTime.UtcNow;
    private int _tickCount;

    public AppSettings Settings { get; private set; }

    public event Action<DownloadItem>? ItemAdded;
    public event Action<DownloadItem>? ItemRemoved;
    public event Action<DownloadItem>? ItemChanged;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public DownloadManager(string? dataDir = null)
    {
        _dataDir = dataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightDownloader");
        Directory.CreateDirectory(_dataDir);

        Settings = LoadSettings();

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxConnectionsPerServer = 256,
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.ExpectContinue = false;

        _rate = new RateLimiter(Settings.SpeedLimitBytesPerSec);
        _engine = new DownloadEngine(_http, _rate);
        Tools = new ToolProvisioner(_http, _dataDir);
        _video = new YtDlpRunner(Tools);
        _converter = new FfmpegConverter(Tools);

        LoadState();

        _speedTimer = new System.Threading.Timer(_ => Tick(), null, 1000, 1000);
    }

    public HttpClient Http => _http;

    /// <summary>Background-provisioned helper tools (yt-dlp + ffmpeg) for video/convert.</summary>
    public ToolProvisioner Tools { get; }

    public IReadOnlyList<DownloadItem> Items
    {
        get { lock (_gate) return _items.ToList(); }
    }

    public DownloadItem Add(DownloadRequest req, bool autoStart = true)
    {
        if (string.Equals(req.Kind, "video", StringComparison.OrdinalIgnoreCase))
            return AddVideo(req, autoStart);

        var item = new DownloadItem
        {
            Url = req.Url,
            FileName = string.IsNullOrWhiteSpace(req.FileName) ? "" : HttpProbe.Sanitize(req.FileName!),
            SaveFolder = Settings.DownloadFolder,
            Connections = Settings.DefaultConnections,
            TotalSize = req.FileSize,
            Referrer = req.Referrer,
            UserAgent = req.UserAgent,
            Cookies = req.Cookies,
            Headers = req.Headers ?? new()
        };
        if (string.IsNullOrWhiteSpace(item.FileName))
            item.FileName = GuessNameFromUrl(req.Url);

        lock (_gate)
        {
            item.FileName = UniqueName(item.SaveFolder, item.FileName);
            _items.Add(item);
        }

        ItemAdded?.Invoke(item);
        SaveState();

        if (autoStart) Start(item.Id);
        return item;
    }

    public DownloadItem AddVideo(DownloadRequest req, bool autoStart = true)
    {
        var item = new DownloadItem
        {
            Kind = DownloadKind.Video,
            Url = req.Url,
            FileName = "(fetching video…)",
            SaveFolder = Settings.DownloadFolder,
            Referrer = req.Referrer,
            UserAgent = req.UserAgent,
            Cookies = req.Cookies,
            TotalSize = -1
        };
        lock (_gate) _items.Add(item);
        ItemAdded?.Invoke(item);
        SaveState();
        if (autoStart) Start(item.Id);
        return item;
    }

    public DownloadItem AddConvert(string inputPath, string outputName, bool autoStart = true)
    {
        var folder = Path.GetDirectoryName(inputPath) ?? Settings.DownloadFolder;
        var item = new DownloadItem
        {
            Kind = DownloadKind.Convert,
            Url = inputPath,
            SaveFolder = folder,
            FileName = UniqueName(folder, HttpProbe.Sanitize(outputName)),
            TotalSize = -1
        };
        lock (_gate) _items.Add(item);
        ItemAdded?.Invoke(item);
        SaveState();
        if (autoStart) Start(item.Id);
        return item;
    }

    public void StartAll()
    {
        foreach (var i in Items)
            if (i.Status is DownloadStatus.Paused or DownloadStatus.Queued or DownloadStatus.Error)
                Start(i.Id);
    }

    public void Start(Guid id)
    {
        DownloadItem? item;
        lock (_gate) item = _items.FirstOrDefault(i => i.Id == id);
        if (item == null || item.Status == DownloadStatus.Completed) return;
        if (_running.ContainsKey(id)) return;

        int active;
        lock (_gate) active = _items.Count(i => _running.ContainsKey(i.Id));
        if (active >= Settings.MaxConcurrentDownloads)
        {
            item.Status = DownloadStatus.Queued;
            ItemChanged?.Invoke(item);
            SaveState();
            return;
        }

        var cts = new CancellationTokenSource();
        _running[id] = cts;
        item.Status = DownloadStatus.Connecting;
        item.Error = null;
        ItemChanged?.Invoke(item);

        _ = Task.Run(async () =>
        {
            try
            {
                item.Status = DownloadStatus.Downloading;
                ItemChanged?.Invoke(item);
                switch (item.Kind)
                {
                    case DownloadKind.Video:
                        await _video.RunAsync(item, cts.Token).ConfigureAwait(false);
                        break;
                    case DownloadKind.Convert:
                        await _converter.RunAsync(item, cts.Token).ConfigureAwait(false);
                        break;
                    default:
                        await _engine.RunAsync(item, cts.Token).ConfigureAwait(false);
                        break;
                }
                item.Status = DownloadStatus.Completed;
                item.CompletedAt = DateTime.Now;
                item.SpeedBytesPerSec = 0;
                TryDelete(item.ControlPath);
            }
            catch (OperationCanceledException)
            {
                item.Status = DownloadStatus.Paused;
                item.SpeedBytesPerSec = 0;
            }
            catch (Exception ex)
            {
                item.Status = DownloadStatus.Error;
                item.Error = ex.Message;
                item.SpeedBytesPerSec = 0;
            }
            finally
            {
                _running.TryRemove(id, out _);
                _lastBytes.Remove(id);
                SaveState();
                ItemChanged?.Invoke(item);
                PumpQueue();
            }
        });
    }

    public void Pause(Guid id)
    {
        if (_running.TryGetValue(id, out var cts))
        {
            cts.Cancel();
            return;
        }
        DownloadItem? item;
        lock (_gate) item = _items.FirstOrDefault(i => i.Id == id);
        if (item != null && item.Status == DownloadStatus.Queued)
        {
            item.Status = DownloadStatus.Paused;
            ItemChanged?.Invoke(item);
            SaveState();
        }
    }

    public void PauseAll()
    {
        foreach (var i in Items) Pause(i.Id);
    }

    public void Remove(Guid id, bool deleteFile)
    {
        if (_running.TryGetValue(id, out var cts)) cts.Cancel();

        DownloadItem? item;
        lock (_gate)
        {
            item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null) _items.Remove(item);
        }
        if (item == null) return;

        if (deleteFile) TryDelete(item.FullPath);
        TryDelete(item.ControlPath);

        ItemRemoved?.Invoke(item);
        SaveState();
    }

    public void UpdateSettings(AppSettings s)
    {
        Settings = s;
        _rate.SetLimit(s.SpeedLimitBytesPerSec);
        SaveSettings();
        PumpQueue();
    }

    private void PumpQueue()
    {
        List<DownloadItem> toStart = new();
        lock (_gate)
        {
            int active = _items.Count(i => _running.ContainsKey(i.Id));
            foreach (var i in _items)
            {
                if (active >= Settings.MaxConcurrentDownloads) break;
                if (i.Status == DownloadStatus.Queued && !_running.ContainsKey(i.Id))
                {
                    toStart.Add(i);
                    active++;
                }
            }
        }
        foreach (var i in toStart) Start(i.Id);
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        double dt = (now - _lastSpeedTick).TotalSeconds;
        if (dt <= 0) return;
        _lastSpeedTick = now;

        List<DownloadItem> running;
        lock (_gate) running = _items.Where(i => _running.ContainsKey(i.Id)).ToList();

        foreach (var i in running)
        {
            if (i.Kind != DownloadKind.Http) continue; // video/convert report their own speed
            long cur = i.Downloaded;
            long prev = _lastBytes.TryGetValue(i.Id, out var p) ? p : cur;
            _lastBytes[i.Id] = cur;
            i.SpeedBytesPerSec = Math.Max(0, (cur - prev) / dt);
        }

        // Persist progress periodically so a crash/kill can still be resumed.
        if (running.Count > 0 && (++_tickCount % 5 == 0)) SaveState();

        CheckSchedule();
    }

    private string? _lastStartFire;
    private string? _lastStopFire;

    private void CheckSchedule()
    {
        var s = Settings;
        if (!s.SchedulerEnabled) return;
        var now = DateTime.Now;
        var nowKey = now.ToString("yyyy-MM-dd HH:mm");
        var hhmm = now.ToString("HH:mm");
        if (!string.IsNullOrEmpty(s.StartTime) && s.StartTime == hhmm && _lastStartFire != nowKey)
        {
            _lastStartFire = nowKey;
            StartAll();
        }
        if (!string.IsNullOrEmpty(s.StopTime) && s.StopTime == hhmm && _lastStopFire != nowKey)
        {
            _lastStopFire = nowKey;
            PauseAll();
        }
    }

    // ---- persistence -------------------------------------------------------

    private string StatePath => Path.Combine(_dataDir, "state.json");
    private string SettingsPath => Path.Combine(_dataDir, "settings.json");

    private void SaveState()
    {
        try
        {
            List<DownloadItem> snap;
            lock (_gate) snap = _items.ToList();
            var tmp = StatePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snap, Json));
            File.Copy(tmp, StatePath, true);
            File.Delete(tmp);
        }
        catch { /* best effort */ }
    }

    private void LoadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return;
            var list = JsonSerializer.Deserialize<List<DownloadItem>>(File.ReadAllText(StatePath), Json);
            if (list == null) return;
            lock (_gate)
            {
                foreach (var it in list)
                {
                    if (it.Status is DownloadStatus.Downloading or DownloadStatus.Connecting or DownloadStatus.Queued)
                        it.Status = DownloadStatus.Paused; // interrupted; let the user resume
                    _items.Add(it);
                }
            }
        }
        catch { /* ignore corrupt state */ }
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Json) ?? new AppSettings();
        }
        catch { /* ignore */ }
        return new AppSettings();
    }

    private void SaveSettings()
    {
        try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, Json)); }
        catch { /* best effort */ }
    }

    // ---- helpers -----------------------------------------------------------

    private static string GuessNameFromUrl(string url)
    {
        try
        {
            var u = new Uri(url);
            var n = Path.GetFileName(WebUtility.UrlDecode(u.AbsolutePath));
            if (!string.IsNullOrWhiteSpace(n)) return HttpProbe.Sanitize(n);
        }
        catch { /* ignore */ }
        return "download";
    }

    private static string UniqueName(string folder, string name)
    {
        try
        {
            if (!File.Exists(Path.Combine(folder, name))) return name;
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            for (int i = 1; i < 10000; i++)
            {
                var cand = $"{stem} ({i}){ext}";
                if (!File.Exists(Path.Combine(folder, cand))) return cand;
            }
        }
        catch { /* ignore */ }
        return name;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    public void Dispose()
    {
        try { _speedTimer.Dispose(); } catch { }
        foreach (var cts in _running.Values) { try { cts.Cancel(); } catch { } }
        SaveState();
        _http.Dispose();
    }
}
