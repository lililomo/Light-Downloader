# Permission justifications

Paste these into the Chrome Web Store "Privacy practices" tab (each permission has
a justification field) and the Firefox AMO "Notes to reviewer" box.

| Permission | Why it is needed |
|---|---|
| `downloads` | To detect a download the user starts and cancel the browser's copy so the desktop app can take over. |
| `cookies` | To read the originating site's cookies so the desktop app can fetch files that require the user's logged-in session (e.g. files behind a login). |
| `contextMenus` | To add the "Download with Light Downloader" and "Download video from this page" right-click items. |
| `storage` | To remember one setting: whether automatic capture is on or off in this browser. |
| `webRequest`, `webRequestBlocking` (Firefox only) | To recognise a download from its response headers (`Content-Disposition`, `Content-Type`) and hand it to the desktop app before Firefox starts its own download or shows its "Save as" dialog. Only file types the user listed in the desktop app are taken; everything else is left untouched. |
| Content script on all sites (`content.js`) | Shows a small "Download this video" button over a video player while the user points at it, like IDM's download panel. It only looks at `<video>` elements on the page; it reads no page text or form data and changes nothing except adding its own button. The page address is sent to the local app only when the user hovers or clicks the button, so the app can list the available qualities. |
| Host access `<all_urls>` | (1) read cookies for whatever site a captured download comes from; (2) run the video button on any site that has a video; (3) Firefox: see the response headers of downloads; (4) connect to the local desktop app at `http://127.0.0.1`. The extension does not track browsing. |

## Remote code
None. The extension contains no remote or eval'd code. It only sends captured
download info to the user's own desktop app on `http://127.0.0.1`.

## Data handling (for the Chrome data-use disclosures)
- Collects: the URL, cookies, referrer and user-agent **only** for downloads the
  user chooses to capture, and the page address of a video when the user points at
  or clicks the "Download this video" button.
- Use: sent solely to the user's local Light Downloader app to perform the download.
- Not sold, not shared with third parties, not used for tracking, not sent to any
  remote server.

## Changes in 1.2.0 (for the update's review notes)
- Firefox: downloads are now recognised with `webRequest` (new permissions
  `webRequest`, `webRequestBlocking`), so capture also works when Firefox is set to
  "Always ask you where to save files".
- New "Download this video" button over web video players (content script).
- Only the file types and browsers chosen in the desktop app's Options are captured.
