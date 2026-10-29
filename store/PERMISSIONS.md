# Permission justifications

Paste these into the Chrome Web Store "Privacy practices" tab (each permission has
a justification field) and the Firefox AMO "Notes to reviewer" box.

| Permission | Why it is needed |
|---|---|
| `downloads` | To detect a download the user starts and cancel the browser's copy so the desktop app can take over. |
| `cookies` | To read the originating site's cookies so the desktop app can fetch files that require the user's logged-in session (e.g. files behind a login). |
| `contextMenus` | To add the "Download with Light Downloader" and "Download video from this page" right-click items. |
| `storage` | To remember one setting: whether automatic capture is on or off. |
| Host access `<all_urls>` | Two reasons: (1) read cookies for whatever site a captured download comes from; (2) connect to the local desktop app at `http://127.0.0.1`. The extension does **not** read or modify page content, inject scripts, or track browsing. |

## Remote code
None. The extension contains no remote or eval'd code. It only sends captured
download info to the user's own desktop app on `http://127.0.0.1`.

## Data handling (for the Chrome data-use disclosures)
- Collects: the URL, cookies, referrer and user-agent **only** for downloads the
  user chooses to capture.
- Use: sent solely to the user's local Light Downloader app to perform the download.
- Not sold, not shared with third parties, not used for tracking, not sent to any
  remote server.

## If a reviewer objects to `<all_urls>`
The host permission can be switched to request-on-demand (`optional_host_permissions`
+ `permissions.request`) so the user grants broad access only on first capture.
This is a straightforward change if required; ask and it can be provided.
