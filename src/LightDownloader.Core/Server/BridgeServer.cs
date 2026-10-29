using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LightDownloader.Core.Models;
using LightDownloader.Core.Queue;

namespace LightDownloader.Core.Server;

/// <summary>
/// Minimal loopback HTTP server the browser extensions talk to. Implemented on a raw
/// <see cref="TcpListener"/> so it needs no admin rights / URL ACL on Windows.
/// Only requests whose Origin is a browser extension are accepted.
/// </summary>
public sealed class BridgeServer : IDisposable
{
    private readonly DownloadManager _mgr;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public int Port { get; private set; }
    public bool Running { get; private set; }

    public event Action<DownloadItem>? DownloadCaptured;

    /// <summary>
    /// Raised instead of adding the download when "ask where to save" is on; the desktop app
    /// shows its New-download dialog and adds the item once the user confirms.
    /// </summary>
    public event Action<DownloadRequest>? DownloadRequested;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public BridgeServer(DownloadManager mgr) => _mgr = mgr;

    public void Start(int preferredPort)
    {
        foreach (var p in new[] { preferredPort, 7414, 7415, 7416, 7417 }.Distinct())
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, p);
                l.Start();
                _listener = l;
                Port = p;
                Running = true;
                break;
            }
            catch { /* port busy, try next */ }
        }
        if (_listener == null)
            throw new InvalidOperationException("Could not bind a loopback port for the browser bridge.");

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                using var stream = client.GetStream();
                var (method, path, headers, body) = await ReadRequestAsync(stream).ConfigureAwait(false);

                var origin = headers.GetValueOrDefault("Origin", "");
                bool allowed = origin.Length == 0
                               || origin.StartsWith("chrome-extension://")
                               || origin.StartsWith("moz-extension://");

                if (method == "OPTIONS") { await WriteAsync(stream, 204, origin, null).ConfigureAwait(false); return; }
                if (!allowed) { await WriteJsonAsync(stream, 403, origin, new { ok = false, error = "origin not allowed" }).ConfigureAwait(false); return; }

                switch (path)
                {
                    case "/ping":
                        await WriteJsonAsync(stream, 200, origin, new { ok = true, app = "LightDownloader", version = "1.0" }).ConfigureAwait(false);
                        break;

                    case "/add":
                        DownloadRequest? dr = null;
                        try { dr = JsonSerializer.Deserialize<DownloadRequest>(body, Json); } catch { }
                        if (dr == null || string.IsNullOrWhiteSpace(dr.Url))
                        {
                            await WriteJsonAsync(stream, 400, origin, new { ok = false, error = "missing url" }).ConfigureAwait(false);
                            break;
                        }
                        if (_mgr.Settings.AskWhereToSave && DownloadRequested != null)
                        {
                            DownloadRequested.Invoke(dr);
                            await WriteJsonAsync(stream, 200, origin, new { ok = true, pending = true }).ConfigureAwait(false);
                            break;
                        }
                        var item = _mgr.Add(dr, autoStart: true);
                        DownloadCaptured?.Invoke(item);
                        await WriteJsonAsync(stream, 200, origin, new { ok = true, id = item.Id }).ConfigureAwait(false);
                        break;

                    default:
                        await WriteJsonAsync(stream, 404, origin, new { ok = false, error = "not found" }).ConfigureAwait(false);
                        break;
                }
            }
            catch { /* ignore malformed clients */ }
        }
    }

    private static async Task<(string method, string path, Dictionary<string, string> headers, string body)>
        ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[16 * 1024];
        using var acc = new MemoryStream();
        int headerEnd = -1;

        while (headerEnd < 0)
        {
            int n = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (n <= 0) break;
            acc.Write(buffer, 0, n);
            headerEnd = IndexOfDoubleCrlf(acc.GetBuffer(), (int)acc.Length);
            if (acc.Length > 1_000_000) break; // guard
        }

        var all = acc.GetBuffer();
        int len = (int)acc.Length;
        if (headerEnd < 0) return ("", "", new(), "");

        var headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        var method = requestLine.Length > 0 ? requestLine[0] : "";
        var path = requestLine.Length > 1 ? requestLine[1] : "";

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            int c = lines[i].IndexOf(':');
            if (c > 0) headers[lines[i][..c].Trim()] = lines[i][(c + 1)..].Trim();
        }

        int bodyStart = headerEnd + 4;
        int contentLength = headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var clv) ? clv : 0;

        using var bodyBytes = new MemoryStream();
        if (contentLength > 0)
        {
            int already = len - bodyStart;
            if (already > 0) bodyBytes.Write(all, bodyStart, Math.Min(already, contentLength));
            while (bodyBytes.Length < contentLength)
            {
                int n = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (n <= 0) break;
                bodyBytes.Write(buffer, 0, n);
            }
        }

        var body = Encoding.UTF8.GetString(bodyBytes.GetBuffer(), 0, (int)bodyBytes.Length);
        return (method, path, headers, body);
    }

    private static int IndexOfDoubleCrlf(byte[] buf, int len)
    {
        for (int i = 0; i + 3 < len; i++)
            if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10)
                return i;
        return -1;
    }

    private static Task WriteJsonAsync(NetworkStream s, int status, string origin, object payload)
        => WriteAsync(s, status, origin, JsonSerializer.SerializeToUtf8Bytes(payload));

    private static async Task WriteAsync(NetworkStream s, int status, string origin, byte[]? body)
    {
        var allow = string.IsNullOrEmpty(origin) ? "*" : origin;
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {Reason(status)}\r\n");
        sb.Append("Access-Control-Allow-Origin: ").Append(allow).Append("\r\n");
        sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
        sb.Append("Access-Control-Allow-Headers: Content-Type\r\n");
        sb.Append("Connection: close\r\n");
        if (body != null)
        {
            sb.Append("Content-Type: application/json\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        }
        else
        {
            sb.Append("Content-Length: 0\r\n");
        }
        sb.Append("\r\n");

        var head = Encoding.ASCII.GetBytes(sb.ToString());
        await s.WriteAsync(head).ConfigureAwait(false);
        if (body != null) await s.WriteAsync(body).ConfigureAwait(false);
        await s.FlushAsync().ConfigureAwait(false);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        500 => "Internal Server Error",
        _ => "OK"
    };

    public void Dispose()
    {
        try
        {
            Running = false;
            _cts?.Cancel();
            _listener?.Stop();
        }
        catch { /* ignore */ }
    }
}
