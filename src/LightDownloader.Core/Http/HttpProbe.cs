using System.Net;
using System.Net.Http.Headers;
using LightDownloader.Core.Models;

namespace LightDownloader.Core.Http;

/// <summary>Resolves file size, range support and filename before a download starts.</summary>
public static class HttpProbe
{
    public static async Task<ProbeResult> ProbeAsync(HttpClient http, DownloadItem item, CancellationToken ct)
    {
        var result = new ProbeResult { FinalUrl = item.Url };

        using var req = new HttpRequestMessage(HttpMethod.Get, item.Url);
        req.Headers.Range = new RangeHeaderValue(0, 0);
        ApplyHeaders(req, item);

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                                   .ConfigureAwait(false);

        result.FinalUrl = resp.RequestMessage?.RequestUri?.ToString() ?? item.Url;
        result.ContentType = resp.Content.Headers.ContentType?.ToString();
        result.ETag = resp.Headers.ETag?.ToString();
        result.LastModified = resp.Content.Headers.LastModified?.ToString("R");

        if (resp.StatusCode == HttpStatusCode.PartialContent &&
            resp.Content.Headers.ContentRange?.Length is long total)
        {
            result.SupportsRanges = true;
            result.TotalSize = total;
        }
        else
        {
            result.SupportsRanges = false;
            result.TotalSize = resp.Content.Headers.ContentLength ?? -1;
        }

        result.FileName = ResolveFileName(resp, result.FinalUrl);
        return result;
    }

    internal static void ApplyHeaders(HttpRequestMessage req, DownloadItem item)
    {
        if (!string.IsNullOrEmpty(item.UserAgent))
            req.Headers.TryAddWithoutValidation("User-Agent", item.UserAgent);
        if (!string.IsNullOrEmpty(item.Referrer))
            req.Headers.TryAddWithoutValidation("Referer", item.Referrer);
        if (!string.IsNullOrEmpty(item.Cookies))
            req.Headers.TryAddWithoutValidation("Cookie", item.Cookies);
        foreach (var kv in item.Headers)
            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
    }

    private static string ResolveFileName(HttpResponseMessage resp, string finalUrl)
    {
        var cd = resp.Content.Headers.ContentDisposition;
        var name = cd?.FileNameStar ?? cd?.FileName;
        if (!string.IsNullOrWhiteSpace(name))
            return Sanitize(name.Trim('"'));

        try
        {
            var uri = new Uri(finalUrl);
            var fromPath = Path.GetFileName(WebUtility.UrlDecode(uri.AbsolutePath));
            if (!string.IsNullOrWhiteSpace(fromPath))
                return Sanitize(fromPath);
        }
        catch { /* fall through */ }

        return "download";
    }

    public static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();
        return string.IsNullOrEmpty(name) ? "download" : name;
    }
}
