using System.IO.Compression;
using System.Net.NetworkInformation;

namespace LightDownloader.Core.Tools;

/// <summary>
/// Downloads and keeps the external helper tools (yt-dlp + ffmpeg) in the app's own
/// folder. Per the app design these are NOT bundled: the app fetches them in the
/// background after install whenever the network is available, and retries while offline.
/// </summary>
public sealed class ToolProvisioner
{
    private const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    // GitHub-hosted build (fast CDN). gyan.dev's smaller "essentials" build throttles direct
    // downloads to a near-stall, so we use BtbN's build for reliability.
    private const string FfmpegZipUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    private readonly HttpClient _http;
    private int _running;

    public string ToolsDir { get; }

    public event Action? ToolsChanged;

    public ToolProvisioner(HttpClient http, string dataDir)
    {
        _http = http;
        ToolsDir = Path.Combine(dataDir, "tools");
        Directory.CreateDirectory(ToolsDir);
    }

    public string YtDlpPath => Path.Combine(ToolsDir, "yt-dlp.exe");
    public string FfmpegPath => Path.Combine(ToolsDir, "ffmpeg.exe");

    public bool HasYtDlp => File.Exists(YtDlpPath);
    public bool HasFfmpeg => File.Exists(FfmpegPath);
    public bool Ready => HasYtDlp && HasFfmpeg;
    public bool IsBusy => Volatile.Read(ref _running) == 1;

    /// <summary>Fire-and-forget background provisioning. Safe to call on every startup.</summary>
    public void EnsureInBackground()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try { await EnsureAsync().ConfigureAwait(false); }
            finally { Interlocked.Exchange(ref _running, 0); }
        });
    }

    public async Task EnsureAsync(CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < 30 && !Ready; attempt++)
        {
            try
            {
                if (!NetworkInterface.GetIsNetworkAvailable())
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                    continue;
                }

                if (!HasYtDlp) await DownloadYtDlpAsync(ct).ConfigureAwait(false);
                if (!HasFfmpeg) await DownloadFfmpegAsync(ct).ConfigureAwait(false);

                if (Ready) { ToolsChanged?.Invoke(); return; }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* transient (offline, rate limit, etc.) — back off and retry */ }

            await Task.Delay(TimeSpan.FromSeconds(Math.Min(300, 30 * (attempt + 1))), ct).ConfigureAwait(false);
        }
        if (Ready) ToolsChanged?.Invoke();
    }

    private async Task DownloadYtDlpAsync(CancellationToken ct)
    {
        var tmp = YtDlpPath + ".tmp";
        using (var src = await _http.GetStreamAsync(YtDlpUrl, ct).ConfigureAwait(false))
        using (var dst = File.Create(tmp))
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
        File.Move(tmp, YtDlpPath, true);
    }

    private async Task DownloadFfmpegAsync(CancellationToken ct)
    {
        var zipPath = Path.Combine(ToolsDir, "ffmpeg-download.zip");
        using (var src = await _http.GetStreamAsync(FfmpegZipUrl, ct).ConfigureAwait(false))
        using (var dst = File.Create(zipPath))
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in zip.Entries)
            {
                var name = Path.GetFileName(entry.FullName);
                if (name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                {
                    entry.ExtractToFile(Path.Combine(ToolsDir, name), overwrite: true);
                }
            }
        }

        try { File.Delete(zipPath); } catch { /* ignore */ }
    }
}
