using System.Text.Json;
using LightDownloader.Core.Util;

namespace LightDownloader.Core.Video;

/// <summary>One choice in the browser's "Download this video" menu.</summary>
/// <param name="Id">Value for <see cref="Models.DownloadRequest.Format"/>: a max height, "m4a" or "mp3".</param>
public sealed record MediaOption(string Id, string Label, string Detail, long Size);

/// <summary>The qualities yt-dlp reports for a page (from <c>yt-dlp -J</c>).</summary>
public sealed class MediaInfo
{
    public string Title { get; init; } = "";
    public double Duration { get; init; }
    public List<MediaOption> Options { get; init; } = new();

    public static MediaInfo Parse(string json, bool canConvertAudio)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var formats = new List<Fmt>();
        if (root.TryGetProperty("formats", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var f in arr.EnumerateArray()) formats.Add(Fmt.From(f));
        else
            formats.Add(Fmt.From(root)); // single-format extractors put the fields on the root

        double duration = Num(root, "duration");
        var audio = formats.Where(f => f.HasAudio && !f.HasVideo)
            .OrderByDescending(f => f.Ext == "m4a")
            .ThenByDescending(f => f.Size)
            .FirstOrDefault();

        var options = new List<MediaOption>();
        foreach (var group in formats.Where(f => f.HasVideo && f.Height > 0)
                     .GroupBy(f => f.Height)
                     .OrderByDescending(g => g.Key)
                     .Take(8))
        {
            // The format yt-dlp will most likely pick at this height (H.264 / MP4 first).
            var best = group.OrderByDescending(f => f.VCodec.StartsWith("avc1", StringComparison.Ordinal))
                .ThenByDescending(f => f.Ext == "mp4")
                .ThenByDescending(f => f.Size)
                .First();
            long size = best.Size > 0 ? best.Size + (best.HasAudio ? 0 : Math.Max(0, audio?.Size ?? 0)) : -1;
            double fps = group.Max(f => f.Fps);
            string label = $"MP4 {group.Key}p" + (fps > 30 ? ((int)Math.Round(fps)).ToString() : "");
            string detail = best.Width > 0 ? $"{best.Width}×{group.Key}" : $"{group.Key}p";
            options.Add(new MediaOption(group.Key.ToString(), label, detail, size));
        }

        // Sites that only expose a plain file (no codec details): offer it as is.
        if (options.Count == 0 && formats.Any(f => f.HasVideo || f.Unknown))
            options.Add(new MediaOption("best", "Best quality", formats.FirstOrDefault(f => f.Ext.Length > 0)?.Ext.ToUpperInvariant() ?? "", -1));

        if (audio != null)
        {
            options.Add(new MediaOption("m4a", "M4A audio", "AAC", audio.Ext == "m4a" ? audio.Size : -1));
            if (canConvertAudio)
                options.Add(new MediaOption("mp3", "MP3 audio", "320 kbps", duration > 0 ? (long)(duration * 320_000 / 8) : -1));
        }

        return new MediaInfo
        {
            Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
            Duration = duration,
            Options = options
        };
    }

    /// <summary>Text for the browser menu, e.g. "85.3 MB" or "" when unknown.</summary>
    public static string SizeText(long size) => size > 0 ? "~" + Formatting.Bytes(size) : "";

    private static double Num(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private sealed record Fmt(string Ext, string VCodec, string ACodec, int Width, int Height, double Fps, long Size)
    {
        // "none" means the stream is absent; an empty codec means yt-dlp doesn't know.
        public bool HasVideo => VCodec != "none" && (VCodec.Length > 0 || Height > 0);
        public bool HasAudio => ACodec != "none" && ACodec.Length > 0;
        public bool Unknown => VCodec.Length == 0 && ACodec.Length == 0 && Height == 0;

        public static Fmt From(JsonElement f)
        {
            double size = Num(f, "filesize");
            if (size <= 0) size = Num(f, "filesize_approx");
            return new Fmt(Str(f, "ext"), Str(f, "vcodec"), Str(f, "acodec"),
                (int)Num(f, "width"), (int)Num(f, "height"), Num(f, "fps"), (long)size);
        }
    }
}
