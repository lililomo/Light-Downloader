using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace LightDownloader.Core.Update;

/// <summary>The latest release, as published in latest.json next to the installers.</summary>
public sealed class UpdateInfo
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Checks the website for a newer release and downloads its installer. Only an https download
/// from our own server whose SHA-256 matches the published one is accepted.
/// </summary>
public sealed class AppUpdater
{
    public const string ManifestUrl = "https://software.beliaja.id/light-downloader/latest.json";
    private const string DownloadHost = "software.beliaja.id";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;

    public AppUpdater(HttpClient http) => _http = http;

    /// <summary>The newer release, or null when <paramref name="current"/> is up to date.</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        using var req = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
        req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var text = (await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false)).TrimStart('﻿');

        var info = JsonSerializer.Deserialize<UpdateInfo>(text, Json);
        if (info == null || ParseVersion(info.Version) is not { } latest || !IsTrustedUrl(info.Url)
            || info.Sha256.Length != 64 || !info.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The update information on the server is invalid.");
        return latest > Normalize(current) ? info : null;
    }

    /// <summary>Downloads the installer to the temp folder and checks its SHA-256; returns its path.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<(long Done, long Total)>? progress, CancellationToken ct = default)
    {
        if (!IsTrustedUrl(info.Url)) throw new InvalidDataException("The update download address is not trusted.");
        var dir = Path.Combine(Path.GetTempPath(), "LightDownloader-update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"LightDownloaderSetup-{info.Version}.exe");

        using var resp = await _http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? info.Size;
        string hash;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = File.Create(path))
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                sha.AppendData(buffer, 0, n);
                done += n;
                progress?.Report((done, total));
            }
            hash = System.Convert.ToHexString(sha.GetHashAndReset());
        }

        if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { /* ignore */ }
            throw new InvalidDataException("The downloaded update is damaged (its checksum doesn't match). Please try again.");
        }
        return path;
    }

    /// <summary>Deletes installers left from earlier updates (the one still running stays).</summary>
    public static void CleanupDownloads()
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "LightDownloader-update");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir))
                try { File.Delete(f); } catch { /* in use */ }
        }
        catch { /* ignore */ }
    }

    public static Version? ParseVersion(string? text)
        => System.Version.TryParse(text?.Trim(), out var v) ? Normalize(v) : null;

    // "1.2" and "1.2.0.0" both become 1.2.0, so they compare equal.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private static bool IsTrustedUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
           && u.Host.Equals(DownloadHost, StringComparison.OrdinalIgnoreCase);
}
