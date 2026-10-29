using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using LightDownloader.Core.Http;
using LightDownloader.Core.Models;

namespace LightDownloader.Core.Engine;

/// <summary>Downloads a single <see cref="DownloadItem"/> using parallel byte-range connections.</summary>
public sealed class DownloadEngine
{
    private readonly HttpClient _http;
    private readonly RateLimiter _rate;
    private const int BufferSize = 64 * 1024;
    private const int MaxRetriesPerSegment = 5;

    public DownloadEngine(HttpClient http, RateLimiter rate)
    {
        _http = http;
        _rate = rate;
    }

    /// <summary>Runs to completion, or throws <see cref="OperationCanceledException"/> on pause/cancel.</summary>
    public async Task RunAsync(DownloadItem item, CancellationToken ct)
    {
        // Fresh item: probe the server, then lay out the segments.
        if (item.Segments.Count == 0)
        {
            var probe = await HttpProbe.ProbeAsync(_http, item, ct).ConfigureAwait(false);
            item.FinalUrl = probe.FinalUrl;
            item.SupportsRanges = probe.SupportsRanges;
            item.TotalSize = probe.TotalSize;
            item.ETag = probe.ETag;
            item.LastModified = probe.LastModified;
            if (string.IsNullOrWhiteSpace(item.FileName) || item.FileName == "download")
            {
                if (!string.IsNullOrWhiteSpace(probe.FileName))
                    item.FileName = probe.FileName!;
            }
            BuildSegments(item);
        }

        Directory.CreateDirectory(item.SaveFolder);
        PreallocateFile(item);

        var url = item.FinalUrl ?? item.Url;

        if (item.SupportsRanges && item.TotalSize > 0 && item.Segments.Count > 1)
        {
            var tasks = new List<Task>();
            foreach (var seg in item.Segments)
                if (!seg.IsComplete)
                    tasks.Add(DownloadSegmentAsync(url, item, seg, ct));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        else
        {
            var seg = item.Segments[0];
            await DownloadSegmentAsync(url, item, seg, ct).ConfigureAwait(false);
            if (item.TotalSize <= 0) item.TotalSize = seg.Downloaded;
        }
    }

    private static void BuildSegments(DownloadItem item)
    {
        item.Segments.Clear();
        if (item.SupportsRanges && item.TotalSize > 0 && item.Connections > 1)
        {
            // Don't create tiny segments for small files.
            int maxByChunk = (int)Math.Max(1, item.TotalSize / (64 * 1024));
            int n = Math.Max(1, Math.Min(item.Connections, maxByChunk));
            long chunk = item.TotalSize / n;
            long start = 0;
            for (int i = 0; i < n; i++)
            {
                long end = (i == n - 1) ? item.TotalSize - 1 : start + chunk - 1;
                item.Segments.Add(new SegmentState(start, end));
                start = end + 1;
            }
        }
        else
        {
            item.Segments.Add(new SegmentState(0, item.TotalSize > 0 ? item.TotalSize - 1 : -1));
        }
    }

    private static void PreallocateFile(DownloadItem item)
    {
        using var fs = new FileStream(item.FullPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        if (item.TotalSize > 0 && fs.Length != item.TotalSize)
            fs.SetLength(item.TotalSize);
    }

    private async Task DownloadSegmentAsync(string url, DownloadItem item, SegmentState seg, CancellationToken ct)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                await DownloadSegmentOnceAsync(url, item, seg, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) when (attempt++ < MaxRetriesPerSegment)
            {
                await Task.Delay(1000 * attempt, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task DownloadSegmentOnceAsync(string url, DownloadItem item, SegmentState seg, CancellationToken ct)
    {
        bool ranged = item.SupportsRanges && seg.End >= 0;
        using var file = new FileStream(item.FullPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);

        while (seg.End < 0 || seg.Downloaded < seg.Length)
        {
            long from = seg.Start + seg.Downloaded;

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            HttpProbe.ApplyHeaders(req, item);
            if (ranged) req.Headers.Range = new RangeHeaderValue(from, seg.End);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                                        .ConfigureAwait(false);

            if (ranged && resp.StatusCode != HttpStatusCode.PartialContent)
            {
                // Server ignored the Range header.
                if (resp.StatusCode == HttpStatusCode.OK && seg.Start == 0 && item.Segments.Count == 1)
                    ranged = false; // single-segment download: just stream the whole body
                else
                    throw new IOException($"Server did not honor the range request (status {(int)resp.StatusCode}).");
            }
            else
            {
                resp.EnsureSuccessStatusCode();
            }

            file.Position = from;
            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                {
                    if (ranged && seg.End >= 0)
                    {
                        long remaining = seg.Length - seg.Downloaded;
                        if (read > remaining) read = (int)remaining;
                        if (read == 0) break;
                    }
                    await _rate.ThrottleAsync(read, ct).ConfigureAwait(false);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    Interlocked.Add(ref seg.Downloaded, read);
                    if (ranged && seg.Downloaded >= seg.Length) break;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (!ranged) break;                                  // non-ranged: single pass
            if (seg.End >= 0 && seg.Downloaded >= seg.Length) break;
            // otherwise the connection dropped early: loop and re-request the remaining bytes
        }

        await file.FlushAsync(ct).ConfigureAwait(false);
    }
}
