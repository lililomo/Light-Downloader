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
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    public DownloadEngine(HttpClient http, RateLimiter rate)
    {
        _http = http;
        _rate = rate;
    }

    /// <summary>Runs to completion, or throws <see cref="OperationCanceledException"/> on pause/cancel.</summary>
    /// <param name="itemLimiter">Optional per-download throttle applied on top of the global one.</param>
    public async Task RunAsync(DownloadItem item, CancellationToken ct, RateLimiter? itemLimiter = null)
    {
        // Fresh item: probe the server, then lay out the segments.
        if (item.Segments.Count == 0)
        {
            var probe = await ProbeWithRetryAsync(item, ct).ConfigureAwait(false);
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
        item.Status = DownloadStatus.Downloading; // the server answered: leave "Connecting…"

        var url = item.FinalUrl ?? item.Url;

        if (item.SupportsRanges && item.TotalSize > 0 && item.Segments.Count > 1)
        {
            var tasks = new List<Task>();
            foreach (var seg in item.Segments)
                if (!seg.IsComplete)
                    tasks.Add(DownloadSegmentAsync(url, item, seg, itemLimiter, ct));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        else
        {
            var seg = item.Segments[0];
            await DownloadSegmentAsync(url, item, seg, itemLimiter, ct).ConfigureAwait(false);
            if (item.TotalSize <= 0) item.TotalSize = seg.Downloaded;
        }
    }

    /// <summary>Probes with a timeout, retrying transient failures, so a silent server can't hang the download.</summary>
    private async Task<ProbeResult> ProbeWithRetryAsync(DownloadItem item, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);
            try
            {
                return await HttpProbe.ProbeAsync(_http, item, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < 3
                                       && ex is OperationCanceledException or HttpRequestException or IOException)
            {
                await Task.Delay(1000 * attempt, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("The server didn't respond.");
            }
        }
    }

    private static void BuildSegments(DownloadItem item)
    {
        // Build a fresh list and swap it in, so UI readers never see a half-built list.
        var segments = new List<SegmentState>();
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
                segments.Add(new SegmentState(start, end));
                start = end + 1;
            }
        }
        else
        {
            segments.Add(new SegmentState(0, item.TotalSize > 0 ? item.TotalSize - 1 : -1));
        }
        item.Segments = segments;
    }

    private static void PreallocateFile(DownloadItem item)
    {
        using var fs = new FileStream(item.FullPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        if (item.TotalSize > 0 && fs.Length != item.TotalSize)
            fs.SetLength(item.TotalSize);
    }

    private async Task DownloadSegmentAsync(string url, DownloadItem item, SegmentState seg,
        RateLimiter? itemLimiter, CancellationToken ct)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                await DownloadSegmentOnceAsync(url, item, seg, itemLimiter, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } // paused/cancelled
            catch (Exception) when (attempt++ < MaxRetriesPerSegment)
            {
                // Transient error or a stalled connection: reconnect from where this segment stopped.
                await Task.Delay(1000 * attempt, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new IOException("The connection kept stalling.");
            }
        }
    }

    private async Task DownloadSegmentOnceAsync(string url, DownloadItem item, SegmentState seg,
        RateLimiter? itemLimiter, CancellationToken ct)
    {
        bool ranged = item.SupportsRanges && seg.End >= 0;
        using var file = new FileStream(item.FullPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        // Cancelled when the connection goes silent for StallTimeout; the caller then reconnects.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);

        while (seg.End < 0 || seg.Downloaded < seg.Length)
        {
            long from = seg.Start + seg.Downloaded;

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            HttpProbe.ApplyHeaders(req, item);
            if (ranged) req.Headers.Range = new RangeHeaderValue(from, seg.End);

            stall.CancelAfter(StallTimeout);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stall.Token)
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
            using var stream = await resp.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);

            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                while (true)
                {
                    stall.CancelAfter(StallTimeout);
                    int read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), stall.Token).ConfigureAwait(false);
                    stall.CancelAfter(Timeout.InfiniteTimeSpan); // throttling below must not count as a stall
                    if (read <= 0) break;

                    if (ranged && seg.End >= 0)
                    {
                        long remaining = seg.Length - seg.Downloaded;
                        if (read > remaining) read = (int)remaining;
                        if (read == 0) break;
                    }
                    await _rate.ThrottleAsync(read, ct).ConfigureAwait(false);
                    if (itemLimiter != null) await itemLimiter.ThrottleAsync(read, ct).ConfigureAwait(false);
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
