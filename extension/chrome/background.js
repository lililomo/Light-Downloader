// Light Downloader — browser integration.
// Intercepts browser downloads and hands them to the desktop app over a loopback port.
// Works as a Chrome MV3 service worker and as a Firefox MV2 background script.

const PORTS = [7414, 7415, 7416, 7417];
let appBase = null;
let appCheckedAt = 0;
const bypass = new Set(); // urls we hand back to the browser, to avoid a capture loop

async function ensureApp(force = false) {
  const now = Date.now();
  if (!force && appBase && now - appCheckedAt < 5000) return appBase;
  for (const p of PORTS) {
    try {
      const r = await fetch(`http://127.0.0.1:${p}/ping`, { method: 'GET' });
      if (r.ok) {
        const j = await r.json();
        if (j && j.app === 'LightDownloader') {
          appBase = `http://127.0.0.1:${p}`;
          appCheckedAt = now;
          return appBase;
        }
      }
    } catch (e) { /* try next port */ }
  }
  appBase = null;
  appCheckedAt = now;
  return null;
}

async function getEnabled() {
  try { const { enabled } = await chrome.storage.local.get({ enabled: true }); return enabled; }
  catch (e) { return true; }
}

async function cookieHeaderFor(url) {
  try {
    const cookies = await chrome.cookies.getAll({ url });
    return cookies.map(c => `${c.name}=${c.value}`).join('; ');
  } catch (e) { return ''; }
}

async function sendToApp(payload) {
  const base = await ensureApp();
  if (!base) return false;
  try {
    const r = await fetch(`${base}/add`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    return r.ok;
  } catch (e) {
    appBase = null;
    return false;
  }
}

function isHttp(u) { return !!u && (u.startsWith('http://') || u.startsWith('https://')); }

chrome.downloads.onCreated.addListener(async (item) => {
  try {
    if (!(await getEnabled())) return;
    const url = item.finalUrl || item.url;
    if (!isHttp(url)) return;

    if (bypass.has(url)) { bypass.delete(url); return; } // our own fallback: let it through

    // Only intercept when the desktop app is actually reachable; otherwise let the browser download.
    const base = await ensureApp();
    if (!base) return;

    try { await chrome.downloads.cancel(item.id); } catch (e) { }
    try { await chrome.downloads.erase({ id: item.id }); } catch (e) { }

    const name = item.filename ? item.filename.split(/[\\/]/).pop() : undefined;
    const ok = await sendToApp({
      url,
      filename: name,
      referrer: item.referrer || '',
      userAgent: navigator.userAgent,
      cookies: await cookieHeaderFor(url),
      fileSize: (item.fileSize && item.fileSize > 0) ? item.fileSize : (item.totalBytes || -1)
    });

    if (!ok) {
      // App disappeared between the ping and the send: give the download back to the browser.
      bypass.add(url);
      try { await chrome.downloads.download({ url }); } catch (e) { }
    }
  } catch (e) { /* never break the browser's own downloads */ }
});

chrome.runtime.onInstalled.addListener(() => {
  try {
    chrome.contextMenus.create({
      id: 'ldm-download',
      title: 'Download with Light Downloader',
      contexts: ['link', 'image', 'video', 'audio']
    });
    chrome.contextMenus.create({
      id: 'ldm-video',
      title: 'Download video from this page (Light Downloader)',
      contexts: ['page', 'video', 'link']
    });
  } catch (e) { }
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId === 'ldm-video') {
    const purl = info.linkUrl || (tab && tab.url) || info.pageUrl;
    if (!isHttp(purl)) return;
    const ok = await sendToApp({
      url: purl,
      kind: 'video',
      referrer: (tab && tab.url) || purl,
      userAgent: navigator.userAgent,
      cookies: await cookieHeaderFor(purl)
    });
    if (!ok) notify('Desktop app not running', 'Start Light Downloader, then try again.');
    return;
  }

  const url = info.linkUrl || info.srcUrl;
  if (!isHttp(url)) return;
  const ok = await sendToApp({
    url,
    referrer: (tab && tab.url) || info.pageUrl || '',
    userAgent: navigator.userAgent,
    cookies: await cookieHeaderFor(url)
  });
  if (!ok) notify('Desktop app not running', 'Start Light Downloader, then try again.');
});

function notify(title, message) {
  // Surface errors as a toolbar-icon badge (keeps the permission set minimal — no "notifications").
  try {
    const action = chrome.action || chrome.browserAction;
    if (!action) return;
    action.setBadgeText({ text: '!' });
    if (action.setBadgeBackgroundColor) action.setBadgeBackgroundColor({ color: '#b42318' });
    if (action.setTitle) action.setTitle({ title: title + ' — ' + message });
    setTimeout(() => { try { action.setBadgeText({ text: '' }); } catch (e) { } }, 6000);
  } catch (e) { }
}
