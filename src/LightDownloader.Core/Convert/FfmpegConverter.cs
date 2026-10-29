using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using LightDownloader.Core.Models;
using LightDownloader.Core.Tools;

namespace LightDownloader.Core.Convert;

/// <summary>Converts/remuxes a local media file to another format with ffmpeg.</summary>
public sealed class FfmpegConverter
{
    private readonly ToolProvisioner _tools;

    private static readonly Regex DurationRx = new(@"Duration:\s*(\d+):(\d+):(\d+\.\d+)", RegexOptions.Compiled);
    private static readonly Regex TimeRx = new(@"time=\s*(\d+):(\d+):(\d+\.\d+)", RegexOptions.Compiled);

    public FfmpegConverter(ToolProvisioner tools) => _tools = tools;

    public async Task RunAsync(DownloadItem item, CancellationToken ct)
    {
        if (!_tools.HasFfmpeg)
            throw new InvalidOperationException("ffmpeg is still downloading in the background. Try again shortly.");

        var input = item.Url;          // source file path lives in Url for Convert items
        var output = item.FullPath;
        if (!File.Exists(input)) throw new FileNotFoundException("Source file not found.", input);
        Directory.CreateDirectory(item.SaveFolder);

        var psi = new ProcessStartInfo
        {
            FileName = _tools.FfmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(input);
        foreach (var a in ArgsForTarget(Path.GetExtension(output))) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add(output);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        double totalSec = 0;
        var errTail = new Queue<string>();

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not { } line) return;
            errTail.Enqueue(line);
            while (errTail.Count > 10) errTail.Dequeue();

            if (totalSec <= 0)
            {
                var d = DurationRx.Match(line);
                if (d.Success) totalSec = ToSeconds(d.Groups[1].Value, d.Groups[2].Value, d.Groups[3].Value);
            }
            var t = TimeRx.Match(line);
            if (t.Success && totalSec > 0)
            {
                double cur = ToSeconds(t.Groups[1].Value, t.Groups[2].Value, t.Groups[3].Value);
                item.PercentOverride = Math.Clamp(cur / totalSec * 100.0, 0, 100);
            }
        };

        proc.Start();
        proc.BeginErrorReadLine();
        _ = proc.StandardOutput.ReadToEndAsync(ct);

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            try { if (File.Exists(output)) File.Delete(output); } catch { }
            throw;
        }

        if (proc.ExitCode != 0)
            throw new Exception(errTail.Count > 0 ? string.Join(" | ", errTail) : $"ffmpeg exited with code {proc.ExitCode}");

        item.PercentOverride = 100;
        if (File.Exists(output)) item.TotalSize = new FileInfo(output).Length;
    }

    private static IEnumerable<string> ArgsForTarget(string ext) => ext.ToLowerInvariant() switch
    {
        ".mp3" => new[] { "-vn", "-b:a", "192k" },
        ".m4a" or ".aac" => new[] { "-vn", "-c:a", "aac", "-b:a", "192k" },
        ".wav" => new[] { "-vn" },
        ".mp4" => new[] { "-c:v", "libx264", "-preset", "veryfast", "-c:a", "aac" },
        ".mkv" => new[] { "-c", "copy" },
        _ => Array.Empty<string>()
    };

    private static double ToSeconds(string h, string m, string s)
        => int.Parse(h) * 3600 + int.Parse(m) * 60 + double.Parse(s, CultureInfo.InvariantCulture);
}
