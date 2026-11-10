using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LightDownloader.Core.Tools;

/// <summary>
/// Downloads and keeps the external helper tools (yt-dlp, ffmpeg and deno) in the app's own
/// folder. Per the app design these are NOT bundled: the app fetches them in the
/// background after install whenever the network is available, and retries while offline.
/// </summary>
public sealed class ToolProvisioner
{
    private const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    // GitHub-hosted build (fast CDN). gyan.dev's smaller "essentials" build throttles direct
    // downloads to a near-stall, so we use BtbN's build for reliability.
    private const string FfmpegZipUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
    // yt-dlp needs a JavaScript runtime for YouTube when the browser's cookies are used (signed-in,
    // members-only, age-restricted videos); without one it finds no formats. Deno is the runtime
    // yt-dlp enables by default and recommends.
    private static readonly string DenoZipUrl =
        "https://github.com/denoland/deno/releases/latest/download/deno-"
        + (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x86_64")
        + "-pc-windows-msvc.zip";

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
    public string DenoPath => Path.Combine(ToolsDir, "deno.exe");

    public bool HasYtDlp => File.Exists(YtDlpPath);
    public bool HasFfmpeg => File.Exists(FfmpegPath);
    public bool HasDeno => File.Exists(DenoPath);
    public bool Ready => HasYtDlp && HasFfmpeg && HasDeno;
    public bool IsBusy => Volatile.Read(ref _running) == 1;

    /// <summary>
    /// The JavaScript runtime to hand yt-dlp as (yt-dlp runtime name, path): our own deno, else a
    /// Node.js the user already has installed (used while deno is still downloading), else null.
    /// </summary>
    public (string Name, string Path)? FindJsRuntime()
    {
        if (HasDeno) return ("deno", DenoPath);
        var node = FindOnPath("node.exe") ?? ExistingFile(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"));
        return node != null ? ("node", node) : null;
    }

    private static string? FindOnPath(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                if (dir.Length > 0 && ExistingFile(Path.Combine(dir.Trim().Trim('"'), exe)) is { } found) return found;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    private static string? ExistingFile(string path) => File.Exists(path) ? path : null;

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
                if (!HasDeno) await DownloadDenoAsync(ct).ConfigureAwait(false);

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

    /// <summary>Deno's zip, checked against the SHA-256 published next to it, then deno.exe from it.</summary>
    private async Task DownloadDenoAsync(CancellationToken ct)
    {
        var sums = await _http.GetStringAsync(DenoZipUrl + ".sha256sum", ct).ConfigureAwait(false);
        var expected = Regex.Match(sums, @"\b[0-9a-fA-F]{64}\b").Value;
        if (expected.Length == 0) throw new InvalidDataException("No SHA-256 published for deno.");

        var zipPath = Path.Combine(ToolsDir, "deno-download.zip");
        try
        {
            using (var src = await _http.GetStreamAsync(DenoZipUrl, ct).ConfigureAwait(false))
            using (var dst = File.Create(zipPath))
                await src.CopyToAsync(dst, ct).ConfigureAwait(false);

            string actual;
            using (var fs = File.OpenRead(zipPath))
                actual = System.Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The deno download is damaged.");

            var tmp = DenoPath + ".tmp";
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var entry = zip.Entries.FirstOrDefault(e => Path.GetFileName(e.FullName).Equals("deno.exe", StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidDataException("deno.exe is missing from the download.");
                entry.ExtractToFile(tmp, overwrite: true);
            }
            File.Move(tmp, DenoPath, true);
        }
        finally
        {
            try { File.Delete(zipPath); } catch { /* ignore */ }
        }
    }
}
