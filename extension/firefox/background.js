// Light Downloader — browser integration.
// Hands browser downloads to the desktop app over a loopback port, and answers the
// "Download this video" button that content.js shows on web players.
// Works as a Chrome MV3 service worker and as a Firefox MV2 background script.

const api = globalThis.browser || globalThis.chrome;
const IS_FIREFOX = typeof globalThis.browser !== 'undefined' && !!browser.runtime.getBrowserInfo;
const PORTS = [7414, 7415, 7416, 7417];
const BROWSER = detectBrowser();
const bypass = new Set(); // urls we hand back to the browser, to avoid a capture loop
const newTabs = new Map(); // tabId → time opened (Firefox: close the blank tab a captured link opened)

// Types the browser shows itself; anything else (or "attachment") is a download.
const INLINE_TYPES = /^(text\/|image\/|video\/|audio\/|multipart\/|application\/(pdf|xhtml\+xml|xml|json|javascript|x-javascript|ecmascript|rss\+xml|atom\+xml|ld\+json|manifest\+json)$)/;

let app = null;          // { base, config } of the running desktop app
let appCheckedAt = 0;
let appLookup = null;

function detectBrowser() {
  if (IS_FIREFOX) return 'firefox';
  const brands = ((navigator.userAgentData && navigator.userAgentData.brands) || []).map(b => b.brand).join(' ');
  const ua = navigator.userAgent;
  if (/Edge/i.test(brands) || /\bEdg\//.test(ua)) return 'edge';
  if (/Opera/i.test(brands) || /\bOPR\//.test(ua)) return 'opera';
  if (/Brave/i.test(brands) || navigator.brave) return 'brave';
  return 'chrome';
}

// Finds the desktop app and its capture settings (cached for 5 s).
function getApp(force = false) {
  if (!force && Date.now() - appCheckedAt < 5000) return Promise.resolve(app);
  if (!appLookup) {
    appLookup = (async () => {
      let found = null;
      for (const p of PORTS) {
        const base = `http://127.0.0.1:${p}`;
        try {
          const r = await fetch(`${base}/config?browser=${BROWSER}`);
          const j = r.ok ? await r.json() : null;
          if (j && j.app === 'LightDownloader') { found = { base, config: j }; break; }
          if (r.status === 404) {
            // Light Downloader 1.1 and older have no /config: capture everything, no video button.
            const pj = await (await fetch(`${base}/ping`)).json();
            if (pj && pj.app === 'LightDownloader') {
              found = { base, config: { capture: true, fileTypes: null, skipSites: [], videoButton: false } };
              break;
            }
          }
        } catch (e) { /* not on this port */ }
      }
      app = found;
      appCheckedAt = Date.now();
      appLookup = null;
      return found;
    })();
  }
  return appLookup;
}

async function localEnabled() {
  try { const { enabled } = await api.storage.local.get({ enabled: true }); return enabled !== false; }
  catch (e) { return true; }
}

async function post(path, payload) {
  const a = await getApp();
  if (!a) return null;
  try {
    const r = await fetch(a.base + path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    return r.ok ? await r.json() : null;
  } catch (e) {
    appCheckedAt = 0;
    return null;
  }
}

async function cookieHeaderFor(url) {
  try {
    const cookies = await api.cookies.getAll({ url });
    return cookies.map(c => `${c.name}=${c.value}`).join('; ');
  } catch (e) { return ''; }
}

async function requestFor(url, extra) {
  return Object.assign({ url, userAgent: navigator.userAgent, cookies: await cookieHeaderFor(url), browser: BROWSER }, extra);
}

function isHttp(u) { return !!u && (u.startsWith('http://') || u.startsWith('https://')); }

function baseName(path) { return path ? path.split(/[\\/]/).pop() : ''; }

function fileNameFromUrl(url) {
  try { return decodeURIComponent(new URL(url).pathname.split('/').pop() || ''); } catch (e) { return ''; }
}

function fileNameFromDisposition(cd) {
  if (!cd) return '';
  let m = /filename\*\s*=\s*([^']*)'[^']*'([^;]+)/i.exec(cd);
  if (m) { try { return decodeURIComponent(m[2].trim().replace(/^"|"$/g, '')); } catch (e) { } }
  m = /filename\s*=\s*("([^"]*)"|[^;]+)/i.exec(cd);
  return m ? baseName((m[2] !== undefined ? m[2] : m[1]).trim()) : '';
}

// IDM-style file type list: "ZIP", "R0*" (wildcard). null = everything (old app).
function typeWanted(name, types) {
  if (!types) return true;
  const m = /\.([a-z0-9]{1,10})$/i.exec(name || '');
  if (!m) return false;
  const ext = m[1].toUpperCase();
  return types.some(t => t.endsWith('*') ? ext.startsWith(t.slice(0, -1)) : ext === t);
}

function siteSkipped(url, sites) {
  if (!url || !sites || !sites.length) return false;
  let host;
  try { host = new URL(url).hostname.toLowerCase(); } catch (e) { return false; }
  return sites.some(s => {
    const d = s.toLowerCase().replace(/^[a-z]+:\/\//, '').replace(/\/.*$/, '').replace(/^\*\./, '');
    return d && (host === d || host.endsWith('.' + d));
  });
}

// The app (if running) when this download should go to it; null to leave it to the browser.
async function captureTarget(url, name, referrer) {
  if (!isHttp(url) || !(await localEnabled())) return null;
  const a = await getApp();
  if (!a || !a.config.capture) return null;
  if (!typeWanted(name || fileNameFromUrl(url), a.config.fileTypes)) return null;
  if (siteSkipped(url, a.config.skipSites) || siteSkipped(referrer, a.config.skipSites)) return null;
  return a;
}

// ---- Chrome (and Edge, Opera, Brave): decide when the browser names the file, before any Save As prompt.

if (!IS_FIREFOX) {
  chrome.downloads.onDeterminingFilename.addListener((item, suggest) => {
    captureChromeDownload(item).catch(() => { }).finally(() => { try { suggest(); } catch (e) { } });
    return true; // suggest() is called asynchronously
  });
}

async function captureChromeDownload(item) {
  if (item.byExtensionId && item.byExtensionId === chrome.runtime.id) return; // our own fallback download
  const url = item.finalUrl || item.url;
  if (bypass.has(url)) { bypass.delete(url); return; }
  const name = baseName(item.filename) || fileNameFromUrl(url);
  if (!(await captureTarget(url, name, item.referrer))) return;

  const res = await post('/add', await requestFor(url, {
    filename: name,
    referrer: item.referrer || '',
    fileSize: item.fileSize > 0 ? item.fileSize : (item.totalBytes > 0 ? item.totalBytes : -1)
  }));
  if (!res || !res.ok) return; // app gone: the browser keeps the download
  try { await chrome.downloads.cancel(item.id); } catch (e) { }
  try { await chrome.downloads.erase({ id: item.id }); } catch (e) { }
}

// ---- Firefox: take the download when its response arrives, before Firefox's own dialog or download starts.

if (IS_FIREFOX) {
  browser.webRequest.onHeadersReceived.addListener(captureFirefoxResponse,
    { urls: ['http://*/*', 'https://*/*'], types: ['main_frame', 'sub_frame'] },
    ['blocking', 'responseHeaders']);
  browser.tabs.onCreated.addListener(tab => {
    const now = Date.now();
    for (const [id, at] of newTabs) if (now - at > 60000) newTabs.delete(id);
    newTabs.set(tab.id, now);
  });
}

async function captureFirefoxResponse(d) {
  if (d.method !== 'GET' || d.statusCode < 200 || d.statusCode >= 300) return {};
  if (bypass.has(d.url)) { bypass.delete(d.url); return {}; }
  const h = {};
  for (const x of d.responseHeaders || []) h[x.name.toLowerCase()] = x.value || '';
  const disposition = h['content-disposition'] || '';
  const type = (h['content-type'] || '').split(';')[0].trim().toLowerCase();
  if (!/^\s*attachment/i.test(disposition) && type && INLINE_TYPES.test(type)) return {}; // a page, not a download

  const name = fileNameFromDisposition(disposition) || fileNameFromUrl(d.url);
  const referrer = d.originUrl || d.documentUrl || '';
  if (!(await captureTarget(d.url, name, referrer))) return {};

  const size = parseInt(h['content-length'], 10);
  const res = await post('/add', await requestFor(d.url, { filename: name, referrer, fileSize: size > 0 ? size : -1 }));
  if (!res || !res.ok) return {};
  if (d.type === 'main_frame') closeBlankTab(d.tabId, d.url);
  return { cancel: true };
}

// A download link opened in a new tab leaves it blank once we take the download: close it, as Firefox would.
async function closeBlankTab(tabId, url) {
  const opened = newTabs.get(tabId);
  if (tabId < 0 || !opened || Date.now() - opened > 15000) return;
  try {
    const t = await browser.tabs.get(tabId);
    if (!t.url || t.url === 'about:blank' || t.url === url) await browser.tabs.remove(tabId);
  } catch (e) { }
}

// ---- Context menu.

api.runtime.onInstalled.addListener(() => {
  try {
    api.contextMenus.create({
      id: 'ldm-download',
      title: 'Download with Light Downloader',
      contexts: ['link', 'image', 'video', 'audio']
    });
    api.contextMenus.create({
      id: 'ldm-video',
      title: 'Download video from this page (Light Downloader)',
      contexts: ['page', 'video', 'link']
    });
  } catch (e) { }
});

api.contextMenus.onClicked.addListener(async (info, tab) => {
  const page = (tab && tab.url) || info.pageUrl || '';
  let res;
  if (info.menuItemId === 'ldm-video') {
    const url = info.linkUrl || page;
    if (!isHttp(url)) return;
    res = await post('/add', await requestFor(url, { kind: 'video', referrer: page }));
  } else {
    const url = info.linkUrl || info.srcUrl;
    if (!isHttp(url)) return;
    res = await post('/add', await requestFor(url, { referrer: page }));
  }
  if (!res) notify('Desktop app not running', 'Start Light Downloader, then try again.');
});

// ---- Messages from content.js (video button) and the popup.

api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  handleMessage(msg || {}, sender).then(sendResponse, e => sendResponse({ ok: false, error: String((e && e.message) || e) }));
  return true;
});

const NOT_RUNNING = { ok: false, error: 'Light Downloader is not running. Start it, then try again.' };

async function handleMessage(msg, sender) {
  const page = msg.pageUrl || (sender.tab && sender.tab.url) || '';
  switch (msg.type) {
    case 'status': {
      const a = await getApp(true);
      return { ok: true, running: !!a, port: a ? a.base.split(':').pop() : null, capture: !!(a && a.config.capture), browser: BROWSER };
    }
    case 'video-config': {
      if (!(await localEnabled())) return { show: false };
      const a = await getApp();
      return { show: !!(a && a.config.videoButton) };
    }
    case 'video-formats': {
      if (!isHttp(msg.url)) return { ok: false, error: 'This video can\'t be downloaded.' };
      return (await post('/media/formats', await requestFor(msg.url, { referrer: page }))) || NOT_RUNNING;
    }
    case 'video-download': {
      if (!isHttp(msg.url)) return { ok: false, error: 'This video can\'t be downloaded.' };
      const payload = msg.direct
        ? await requestFor(msg.url, { referrer: page, filename: msg.filename || '' })
        : await requestFor(msg.url, { kind: 'video', format: msg.format, title: msg.title || '', referrer: page });
      return (await post('/add', payload)) || NOT_RUNNING;
    }
  }
  return { ok: false };
}

function notify(title, message) {
  // Surface errors as a toolbar-icon badge (keeps the permission set minimal — no "notifications").
  try {
    const action = api.action || api.browserAction;
    if (!action) return;
    action.setBadgeText({ text: '!' });
    if (action.setBadgeBackgroundColor) action.setBadgeBackgroundColor({ color: '#b42318' });
    if (action.setTitle) action.setTitle({ title: title + ' — ' + message });
    setTimeout(() => { try { action.setBadgeText({ text: '' }); } catch (e) { } }, 6000);
  } catch (e) { }
}
