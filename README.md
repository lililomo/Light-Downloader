# Light Downloader

A lightweight, IDM-style download manager for Windows with Chrome/Firefox integration.
Native .NET (WPF) desktop app + a loopback bridge the browser extensions talk to.

Download: https://beliaja.id/light-downloader/

## Features

- **IDM-style interface** — a plain Windows window: menu bar, toolbar, categories tree, and a
  download list with File Name / Size / Status / Time left / Transfer rate / Last Try Date columns
  (sortable) and Explorer's file-type icons.
- **Per-download progress window** — IDM's dialog: Download status / Speed Limiter / Options on
  completion tabs, segment map (one block per connection) and connection table.
- **Ask where to save** — every new download (typed or captured from the browser) opens a
  "Download File Info" dialog with the size, category (auto-detected) and Save As path; the path can
  be remembered per category.
- **Options** — General (start with Windows, which browsers to capture, video button), File Types
  (IDM's default list of extensions to capture, sites to skip), Connection, Save To, Downloads.
- **Change location** — move or rename any download, partial or finished.
- **Multi-connection downloads** — splits each file into parallel byte-range connections (default 8) for speed.
- **Resume** — survives pause and app restart (progress is saved with the download list).
- **Robust connections** — server timeouts and stalled connections are detected and reconnected automatically.
- **Queue** — limits how many downloads run at once; the rest wait.
- **Speed limiter** — global cap, plus an optional per-download cap in the progress window.
- **Browser capture** — Chrome (MV3) + Firefox extensions take over downloads of the listed file types and hand the URL + cookies + referrer/User-Agent to the app. Chrome decides when the browser names the file (`downloads.onDeterminingFilename`); Firefox when the response arrives (`webRequest`), so Firefox's own Save dialog never appears first.
- **Video grabber** — a "Download this video" button over web players (like IDM's panel) lists the qualities yt-dlp finds (e.g. 2160p … 144p, M4A, MP3); "Video" / right-click "Download video from this page" do the same from the app. Video and audio are merged with **ffmpeg**. When yt-dlp doesn't know the page, the menu still offers what the player itself plays: "Video file" for a plain file, or "Video stream" for an HLS/DASH playlist (`.m3u8` / `.mpd`, saved through yt-dlp under the page title).
- **Media converter** — convert/remux a finished file (MP4 / MKV / MP3 / M4A / WAV) with ffmpeg.
- **Simple scheduler** — optional daily "start all at HH:mm" / "pause all at HH:mm".
- **System tray** — closing the window hides to tray; the app keeps running.
- **Automatic updates** — at startup (and every 12 hours) the app reads `latest.json` from
  software.beliaja.id. When a newer version is out it shows an update prompt (Update now / Remind me
  later / Skip this version); *Update now* downloads the installer, checks its SHA-256, runs it
  silently and starts the new version. *Help → Check for updates...* checks on demand; it can be
  turned off in Options → General.

ffmpeg, yt-dlp and deno are **not bundled**. On first run the app downloads them in the
background (into `%AppData%\LightDownloader\tools`) whenever the network is available,
and retries while offline. Deno is the JavaScript runtime yt-dlp needs for YouTube when the
browser's sign-in is used (members-only, age-restricted videos); until it is there, an installed
Node.js is used instead. YouTube is tried signed out first, and with the browser's cookies only
when the video needs them.

## Requirements

- Windows 10/11
- .NET 8 Desktop Runtime (already present on this machine)

## Install

Double-click **`installer/LightDownloaderSetup.exe`**, a standard Windows setup wizard
(English and Indonesian). It installs to `%LocalAppData%\Programs\Light Downloader` by default,
adds Start Menu and optional Desktop shortcuts, registers an entry in *Add/Remove Programs*, and
can launch the app when it finishes (the app then downloads ffmpeg, yt-dlp and deno in the background).
On the last page it also offers to open the Chrome Web Store page in Chrome and the Firefox
Add-ons page in Firefox, for each browser that is installed and doesn't have the extension yet.
No administrator rights are needed for the app itself. If the .NET 8 Desktop Runtime is missing,
the wizard offers to download and install it from Microsoft, which asks for elevation.

Uninstall via *Add/Remove Programs*.

Rebuild the installer after code changes with `build-installer.ps1` (needs
[Inno Setup 6](https://jrsoftware.org/isdl.php)). The wizard script is `setup/LightDownloader.iss`.

### Releasing an update

1. Raise `<Version>` in `src/LightDownloader.App/LightDownloader.App.csproj` and write the
   "What's new" text in `setup/release-notes.txt`.
2. Run `build-installer.ps1`. It writes `installer/LightDownloaderSetup.exe` and
   `installer/latest.json` (version, download URL, SHA-256, size, notes).
3. Upload the installer to `https://software.beliaja.id/light-downloader/<version>/LightDownloaderSetup.exe`
   **first**, then `latest.json` to `https://software.beliaja.id/light-downloader/latest.json`.
   Installed copies offer the update from then on. The app only accepts an https download from
   software.beliaja.id whose SHA-256 matches `latest.json`.

The app icon is vector art in `art/IconGen`; `build-icons.ps1` regenerates the `.ico`, the
extension icons and every store asset in `store/icons`.

## Build & run

```bash
dotnet build "LightDownloader.sln" -c Release
dotnet run --project "src/LightDownloader.App/LightDownloader.App.csproj" -c Release
```

The built executable: `src/LightDownloader.App/bin/Release/net8.0-windows/LightDownloader.exe`

## Install the browser extensions

From the stores: [Chrome Web Store](https://chromewebstore.google.com/detail/light-downloader-integrat/oaghadmejhjmmkagkdimogebdncmmpci)
· [Firefox Add-ons](https://addons.mozilla.org/en-US/firefox/addon/light-downloader-integration/).

For development:

**Chrome / Edge** — go to `chrome://extensions`, enable *Developer mode*, click
*Load unpacked*, and select `extension/chrome`.

**Firefox** — go to `about:debugging#/runtime/this-firefox`, click
*Load Temporary Add-on…*, and select `extension/firefox/manifest.json`.

With the desktop app running, downloads started in the browser are captured
automatically (toggle it from the extension popup).

## How it works

```
Browser ──(download)──► Extension ──HTTP──► 127.0.0.1:7414 (bridge) ──► Download engine
                                              (extension-origin only)       │
                                                                     segmented ranges,
                                                                     resume, rate limit
Video: Extension/UI ─► app ─► yt-dlp (+ffmpeg merge) ─► file
Convert: UI ─► app ─► ffmpeg ─► file
```

- `src/LightDownloader.Core` — engine, queue/manager, loopback bridge, tool provisioner, yt-dlp/ffmpeg runners.
- `src/LightDownloader.App` — WPF window, tray, dialogs.
- `extension/chrome`, `extension/firefox` — browser integration.

State and settings live in `%AppData%\LightDownloader`.

## Notes / limitations

- **No kernel driver.** Capture works for downloads started *in the browser* (that is
  what the extensions cover — the same mechanism IDM's own extension uses). Downloads
  started by *non-browser* apps aren't auto-captured; add those by URL manually. This
  keeps the app light and avoids a signed kernel driver, admin install, and AV/VPN conflicts.
- The bridge binds to loopback only and accepts requests **only** from `chrome-extension://`
  / `moz-extension://` origins.
- Targets `net8.0` because the .NET 8 desktop runtime is installed here; bump to `net10.0`
  after installing the .NET 10 Desktop Runtime if desired.
