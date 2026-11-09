using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LightDownloader.Core.Models;
using LightDownloader.Core.Tools;

namespace LightDownloader.Core.Video;

/// <summary>Grabs a video with yt-dlp (which uses our ffmpeg to merge video+audio).</summary>
public sealed class YtDlpRunner
{
    private readonly ToolProvisioner _tools;

    // [download]  12.3% of ~45.67MiB at 1.23MiB/s ETA 00:30
    private static readonly Regex ProgressRx = new(
        @"\[download\]\s+([\d.]+)%\s+of\s+~?\s*([\d.]+)\s*([KMGT]?i?B)(?:\s+at\s+([\d.]+)\s*([KMGT]?i?B)/s)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DestRx = new(@"Destination:\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex MergeRx = new(@"Merging formats into\s+""(.+)""", RegexOptions.Compiled);

    public YtDlpRunner(ToolProvisioner tools) => _tools = tools;

    public async Task RunAsync(DownloadItem item, CancellationToken ct)
    {
        if (!_tools.HasYtDlp)
            throw new InvalidOperationException("yt-dlp is still downloading in the background. Try again shortly.");

        Directory.CreateDirectory(item.SaveFolder);

        var psi = new ProcessStartInfo
        {
            FileName = _tools.YtDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--newline");
        psi.ArgumentList.Add("--no-playlist");
        AddFormatArgs(psi.ArgumentList, item.VideoFormat);
        if (_tools.HasFfmpeg)
        {
            psi.ArgumentList.Add("--ffmpeg-location");
            psi.ArgumentList.Add(_tools.ToolsDir);
        }
        AddRequestArgs(psi.ArgumentList, item.Referrer, item.Cookies, item.UserAgent);
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(Path.Combine(item.SaveFolder, "%(title)s.%(ext)s"));
        psi.ArgumentList.Add(item.Url);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        string? finalPath = null;
        var errorTail = new Queue<string>();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not { } line) return;
            var m = ProgressRx.Match(line);
            if (m.Success)
            {
                item.PercentOverride = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var total = ParseSize(m.Groups[2].Value, m.Groups[3].Value);
                if (total > 0) item.TotalSize = total;
                if (m.Groups[4].Success) item.SpeedBytesPerSec = ParseSize(m.Groups[4].Value, m.Groups[5].Value);
            }
            var md = DestRx.Match(line);
            if (md.Success) finalPath = md.Groups[1].Value.Trim();
            var mm = MergeRx.Match(line);
            if (mm.Success) finalPath = mm.Groups[1].Value.Trim();
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not { } line) return;
            errorTail.Enqueue(line);
            while (errorTail.Count > 10) errorTail.Dequeue();
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        if (proc.ExitCode != 0)
            throw new Exception(errorTail.Count > 0 ? string.Join(" | ", errorTail) : $"yt-dlp exited with code {proc.ExitCode}");

        if (!string.IsNullOrEmpty(finalPath))
        {
            try
            {
                var fp = Path.GetFullPath(finalPath);
                item.FileName = Path.GetFileName(fp);
                if (File.Exists(fp)) item.TotalSize = new FileInfo(fp).Length;
            }
            catch { /* keep placeholder name */ }
        }
        item.PercentOverride = 100;
    }

    /// <summary>yt-dlp format selection for a quality picked in the browser or the New-download dialog.</summary>
    private void AddFormatArgs(IList<string> args, string? format)
    {
        bool ffmpeg = _tools.HasFfmpeg;
        switch (format?.ToLowerInvariant())
        {
            case "m4a":
                args.Add("-f"); args.Add("ba[ext=m4a]/ba/b");
                if (ffmpeg) { args.Add("-x"); args.Add("--audio-format"); args.Add("m4a"); }
                return;
            case "mp3" when ffmpeg:
                args.Add("-f"); args.Add("ba/b");
                args.Add("-x"); args.Add("--audio-format"); args.Add("mp3"); args.Add("--audio-quality"); args.Add("0");
                return;
        }
        // Highest resolution up to the chosen height; at equal resolution prefer H.264/AAC so the MP4 plays everywhere.
        args.Add("-f");
        args.Add(int.TryParse(format, out var h) && h > 0 ? $"bv*[height<={h}]+ba/b[height<={h}]/bv*+ba/b" : "bv*+ba/b");
        args.Add("-S"); args.Add("res,vcodec:h264,ext:mp4:m4a");
        args.Add("--merge-output-format"); args.Add("mp4");
    }

    private static void AddRequestArgs(IList<string> args, string? referrer, string? cookies, string? userAgent)
    {
        if (!string.IsNullOrEmpty(referrer)) { args.Add("--referer"); args.Add(referrer); }
        if (!string.IsNullOrEmpty(cookies)) { args.Add("--add-header"); args.Add("Cookie:" + cookies); }
        if (!string.IsNullOrEmpty(userAgent)) { args.Add("--add-header"); args.Add("User-Agent:" + userAgent); }
    }

    /// <summary>Asks yt-dlp (without downloading) which qualities a page offers, for the browser's video button.</summary>
    public async Task<MediaInfo> GetMediaInfoAsync(DownloadRequest req, CancellationToken ct)
    {
        if (!_tools.HasYtDlp)
            throw new InvalidOperationException("The video tools are still downloading. Try again in a minute.");

        var psi = new ProcessStartInfo
        {
            FileName = _tools.YtDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-J");
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add("--no-warnings");
        AddRequestArgs(psi.ArgumentList, req.Referrer, req.Cookies, req.UserAgent);
        psi.ArgumentList.Add(req.Url);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        var json = await stdout.ConfigureAwait(false);
        if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
        {
            var err = (await stderr.ConfigureAwait(false)).Trim();
            var line = err.Split('\n').LastOrDefault(l => l.Contains("ERROR", StringComparison.Ordinal)) ?? err;
            throw new Exception(line.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase)
                ? "No downloadable video was found on this page."
                : line.Replace("ERROR: ", "").Trim());
        }
        return MediaInfo.Parse(json, _tools.HasFfmpeg);
    }

    private static long ParseSize(string num, string unit)
    {
        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return -1;
        double mul = unit.ToUpperInvariant() switch
        {
            "B" => 1,
            "KB" or "KIB" => 1024d,
            "MB" or "MIB" => 1024d * 1024,
            "GB" or "GIB" => 1024d * 1024 * 1024,
            "TB" or "TIB" => 1024d * 1024 * 1024 * 1024,
            _ => 1
        };
        return (long)(v * mul);
    }
}
