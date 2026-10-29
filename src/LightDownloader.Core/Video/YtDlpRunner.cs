using System.Diagnostics;
using System.Globalization;
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
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("bv*+ba/b");
        psi.ArgumentList.Add("--merge-output-format");
        psi.ArgumentList.Add("mp4");
        if (_tools.HasFfmpeg)
        {
            psi.ArgumentList.Add("--ffmpeg-location");
            psi.ArgumentList.Add(_tools.ToolsDir);
        }
        if (!string.IsNullOrEmpty(item.Referrer))
        {
            psi.ArgumentList.Add("--referer");
            psi.ArgumentList.Add(item.Referrer);
        }
        if (!string.IsNullOrEmpty(item.Cookies))
        {
            psi.ArgumentList.Add("--add-header");
            psi.ArgumentList.Add("Cookie:" + item.Cookies);
        }
        if (!string.IsNullOrEmpty(item.UserAgent))
        {
            psi.ArgumentList.Add("--add-header");
            psi.ArgumentList.Add("User-Agent:" + item.UserAgent);
        }
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
