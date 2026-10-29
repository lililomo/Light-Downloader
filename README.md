# Light Downloader

A lightweight, IDM-style download manager for Windows with Chrome/Firefox integration.
Native .NET (WPF) desktop app + a loopback bridge the browser extensions talk to.

## Features

- **Multi-connection downloads** — splits each file into parallel byte-range connections (default 8) for speed.
- **Resume** — survives pause and app restart via a per-file `.ldm` control file.
- **Queue** — limits how many downloads run at once; the rest wait.
- **Speed limiter** — global cap (token bucket), 0 = unlimited.
- **Browser capture** — Chrome (MV3) + Firefox extensions intercept downloads, cancel the browser's own download, and hand the URL + cookies + referrer/User-Agent to the app.
- **Video grabber** — "Add video" / right-click "Download video from this page" grabs video with **yt-dlp** and merges video+audio with **ffmpeg**.
- **Media converter** — convert/remux a finished file (MP4 / MKV / MP3 / M4A / WAV) with ffmpeg.
- **Simple scheduler** — optional daily "start all at HH:mm" / "pause all at HH:mm".
- **System tray** — closing the window hides to tray; the app keeps running.

ffmpeg + yt-dlp are **not bundled**. On first run the app downloads them in the
background (into `%AppData%\LightDownloader\tools`) whenever the network is available,
and retries while offline.

## Requirements

- Windows 10/11
- .NET 8 Desktop Runtime (already present on this machine)

## Install

Double-click **`installer/LightDownloaderSetup.exe`**. It installs to
`%LocalAppData%\Programs\Light Downloader`, adds Start Menu + Desktop shortcuts,
registers an entry in *Add/Remove Programs*, and launches the app (which then
downloads ffmpeg + yt-dlp in the background). No administrator rights needed.

Uninstall via *Add/Remove Programs*, or run:

```
"%LocalAppData%\Programs\Light Downloader\LightDownloaderSetup.exe" --uninstall
```

Rebuild the installer after code changes with `build-installer.ps1`.

## Build & run

```bash
dotnet build "LightDownloader.sln" -c Release
dotnet run --project "src/LightDownloader.App/LightDownloader.App.csproj" -c Release
```

The built executable: `src/LightDownloader.App/bin/Release/net8.0-windows/LightDownloader.exe`

## Install the browser extensions

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
