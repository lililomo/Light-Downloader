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
        await WithCookieAttemptsAsync(item.Url, item.Cookies, async cookies =>
        {
            try
            {
                await RunOnceAsync(item, cookies, ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                item.PercentOverride = null;
                throw;
            }
        }).ConfigureAwait(false);
    }

    private async Task RunOnceAsync(DownloadItem item, string? cookies, CancellationToken ct)
    {
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
        bool hasRuntime = AddJsRuntimeArgs(psi.ArgumentList);
        AddFormatArgs(psi.ArgumentList, item.VideoFormat);
        if (_tools.HasFfmpeg)
        {
            psi.ArgumentList.Add("--ffmpeg-location");
            psi.ArgumentList.Add(_tools.ToolsDir);
        }
        AddRequestArgs(psi.ArgumentList, item.Referrer, cookies, item.UserAgent);
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(Path.Combine(item.SaveFolder, OutputName(item)));
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
        {
            var error = errorTail.LastOrDefault(l => l.Contains("ERROR", StringComparison.Ordinal));
            throw error != null ? Fail(error, item.Url, cookies, hasRuntime)
                : new Exception(errorTail.Count > 0 ? string.Join(" | ", errorTail) : $"yt-dlp exited with code {proc.ExitCode}");
        }

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

    /// <summary>
    /// yt-dlp's output name. A stream playlist (.m3u8 / .mpd, from the browser's "Video stream" choice) has no
    /// title of its own, only a file name like "master" or "index", so the page title the browser sent
    /// (the item's placeholder name) names the file.
    /// </summary>
    private static string OutputName(DownloadItem item)
    {
        if (IsStreamPlaylist(item.Url) && !item.FileName.StartsWith("(", StringComparison.Ordinal))
        {
            var name = Regex.Replace(item.FileName, @"\.(mp4|mkv|webm|m4a|mp3)$", "", RegexOptions.IgnoreCase).Trim();
            if (name.Length > 0) return name.Replace("%", "%%") + ".%(ext)s";
        }
        return "%(title)s.%(ext)s";
    }

    private static bool IsStreamPlaylist(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u)
           && Regex.IsMatch(u.AbsolutePath, @"\.(m3u8|mpd|f4m)$|\.ism[lv]?/manifest", RegexOptions.IgnoreCase);

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

    /// <summary>
    /// Which cookies to try, in order. YouTube is fetched signed out first, which works for most videos
    /// and keeps the user's account out of it; the browser's cookies are the fallback for videos that
    /// need the login (members-only, age-restricted, "confirm you're not a bot"). Signed-in YouTube
    /// requests need a JavaScript runtime (see <see cref="AddJsRuntimeArgs"/>). Other sites get the
    /// cookies first, since they may need the login.
    /// </summary>
    private static string?[] CookieAttempts(string url, string? cookies)
    {
        if (string.IsNullOrEmpty(cookies)) return new string?[] { null };
        return IsYouTube(url) ? new[] { null, cookies } : new[] { cookies, null };
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> with each of <see cref="CookieAttempts"/> until one succeeds. When the
    /// signed-out attempt was refused for needing a login and the signed-in one failed too, the signed-in
    /// error is the one to show (it says why the user's own account didn't get the video).
    /// </summary>
    private static async Task<T> WithCookieAttemptsAsync<T>(string url, string? cookies, Func<string?, Task<T>> attempt)
    {
        Exception? first = null, signedOut = null, signedIn = null;
        foreach (var c in CookieAttempts(url, cookies))
        {
            try { return await attempt(c).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                first ??= ex;
                if (c == null) signedOut = ex; else signedIn = ex;
            }
        }
        throw signedOut is YtDlpError { NeedsLogin: true } && signedIn != null ? signedIn : first!;
    }

    /// <summary>
    /// Gives yt-dlp a JavaScript runtime (our deno, or the user's Node.js) so YouTube works with the
    /// browser's cookies. Returns false when there is none yet.
    /// </summary>
    private bool AddJsRuntimeArgs(IList<string> args)
    {
        if (_tools.FindJsRuntime() is not { } rt) return false;
        args.Add("--js-runtimes");
        args.Add($"{rt.Name}:{rt.Path}");
        return true;
    }

    private sealed class YtDlpError(string message, bool needsLogin) : Exception(message)
    {
        public bool NeedsLogin { get; } = needsLogin;
    }

    /// <summary>The exception for a yt-dlp error line.</summary>
    private static YtDlpError Fail(string error, string url, string? cookies, bool hasRuntime)
    {
        // Signed in to YouTube without a JavaScript runtime, yt-dlp sees no formats at all.
        if (!hasRuntime && cookies != null && IsYouTube(url)
            && error.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase))
            return new YtDlpError("Light Downloader is still downloading a tool it needs for this video. Try again in a few minutes.", false);
        return new YtDlpError(Friendly(error), NeedsLogin(error));
    }

    private static bool NeedsLogin(string error)
    {
        bool Has(string s) => error.Contains(s, StringComparison.OrdinalIgnoreCase);
        return Has("members-only") || Has("Join this channel") || Has("Sign in") || Has("Private video")
               || Has("login") || Has("log in") || Has("--cookies");
    }

    private static bool IsYouTube(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u)
           && (u.Host == "youtu.be" || u.Host == "youtube.com"
               || u.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)
               || u.Host.EndsWith("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>A yt-dlp error line as a short sentence for the user.</summary>
    private static string Friendly(string error)
    {
        bool Has(string s) => error.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (Has("Unsupported URL")) return "No downloadable video was found on this page.";
        if (Has("members-only") || Has("Join this channel"))
            return "This video is for channel members only. Open it in your browser while signed in with a member account, then try again.";
        if (Has("not a bot")) return "YouTube wants to confirm you're not a bot. Sign in to YouTube in your browser, then try again.";
        if (Has("Sign in to confirm your age")) return "This video is age-restricted. Sign in to the site in your browser, then try again.";
        if (Has("Private video")) return "This video is private.";
        if (Has("Requested format is not available")) return "No downloadable format was found for this video.";
        // "ERROR: [youtube] abc123: message" -> "message"
        return Regex.Replace(error.Replace("ERROR: ", ""), @"^\[[^\]]+\]\s*[^:\s]+:\s*", "").Trim();
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

        return await WithCookieAttemptsAsync(req.Url, req.Cookies, cookies => ProbeOnceAsync(req, cookies, ct)).ConfigureAwait(false);
    }

    private async Task<MediaInfo> ProbeOnceAsync(DownloadRequest req, string? cookies, CancellationToken ct)
    {
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
        bool hasRuntime = AddJsRuntimeArgs(psi.ArgumentList);
        AddRequestArgs(psi.ArgumentList, req.Referrer, cookies, req.UserAgent);
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
            throw Fail(line, req.Url, cookies, hasRuntime);
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
