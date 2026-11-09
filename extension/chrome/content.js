// Light Downloader — "Download this video" button over web players (like IDM's download panel).
// Shown on a playing video while the mouse is over it; the menu lists the qualities the
// desktop app (yt-dlp) finds for this page. Styles are set through the CSSOM so strict page
// CSPs don't block them, and the button lives in a closed shadow root so pages can't restyle it.
(() => {
  if (window.__lightDownloaderButton) return;
  window.__lightDownloaderButton = true;

  const api = globalThis.browser || globalThis.chrome;
  const MIN_W = 240, MIN_H = 135;
  const HIDE_AFTER = 3000;

  let config = null, configAt = 0;
  let current = null;       // video the button is attached to
  let hideTimer = 0, rafId = 0;
  let menuOpen = false;
  const dismissed = new WeakSet();
  const formats = new Map(); // page url → Promise<response>

  // ---- helpers ------------------------------------------------------------

  const css = (el, s) => { Object.assign(el.style, s); return el; };
  const make = (tag, s, text) => { const e = css(document.createElement(tag), s || {}); if (text) e.textContent = text; return e; };

  function send(msg) {
    try { return api.runtime.sendMessage(msg).then(r => r || { ok: false }, () => ({ ok: false })); }
    catch (e) { return Promise.resolve({ ok: false }); } // extension was updated or removed: this page is orphaned
  }

  async function enabled() {
    if (!config || Date.now() - configAt > 30000) {
      config = await send({ type: 'video-config' });
      configAt = Date.now();
    }
    return !!(config && config.show);
  }

  // The page yt-dlp should read. YouTube: the clean watch URL; elsewhere the frame's own address.
  function pageUrl() {
    const u = new URL(location.href);
    if (/(^|\.)youtube\.com$/.test(u.hostname)) {
      const v = u.searchParams.get('v');
      if (u.pathname === '/watch' && v) return `https://www.youtube.com/watch?v=${v}`;
    }
    return location.href;
  }

  // YouTube plays muted previews on its home and search pages; only offer the button on real players.
  function pageAllowsButton() {
    if (!/(^|\.)youtube\.com$/.test(location.hostname)) return true;
    return /^\/(watch|shorts\/|embed\/|live\/)/.test(location.pathname);
  }

  function eligible(v) {
    if (!v || !v.isConnected || dismissed.has(v)) return false;
    const r = v.getBoundingClientRect();
    if (r.width < MIN_W || r.height < MIN_H) return false;
    if (!(v.duration > 3) && v.duration !== Infinity) return false; // skip GIF-like loops
    return v.currentTime > 0 || !v.paused;
  }

  // ---- the button -----------------------------------------------------------

  const host = document.createElement('div');
  css(host, { all: 'initial', position: 'fixed', zIndex: '2147483647', top: '0', right: '0', display: 'none' });
  const root = host.attachShadow({ mode: 'closed' });
  const font = { fontFamily: '"Segoe UI", system-ui, sans-serif', fontSize: '12px', lineHeight: '16px', color: '#1b1b1b' };

  const bar = make('div', Object.assign({ display: 'flex', alignItems: 'stretch', boxShadow: '0 1px 4px rgba(0,0,0,.45)', borderRadius: '3px', overflow: 'hidden' }, font));
  const main = make('div', { display: 'flex', alignItems: 'center', gap: '6px', padding: '4px 9px 4px 7px', background: '#1d6fd6', color: '#fff', cursor: 'pointer', userSelect: 'none', whiteSpace: 'nowrap' });
  main.append(icon(), make('span', {}, 'Download this video'), make('span', { fontSize: '9px', opacity: '.85' }, '▼'));
  const close = make('div', { display: 'flex', alignItems: 'center', padding: '0 7px', background: '#155bb3', color: '#fff', cursor: 'pointer', fontSize: '13px' }, '×');
  close.title = 'Hide for this video';
  bar.append(main, close);

  const menu = make('div', Object.assign({ display: 'none', marginTop: '3px', minWidth: '260px', maxWidth: '360px', background: '#fff', border: '1px solid #a0a0a0', boxShadow: '2px 3px 8px rgba(0,0,0,.35)', padding: '3px 0' }, font));
  // Right-aligned column: the bar keeps its size and the menu opens under it, growing to the left.
  const box = make('div', { display: 'flex', flexDirection: 'column', alignItems: 'flex-end' });
  box.append(bar, menu);
  root.append(box);

  for (const [el, bg, over] of [[main, '#1d6fd6', '#2b7de4'], [close, '#155bb3', '#c42b1c']]) {
    el.addEventListener('mouseenter', () => { el.style.background = over; });
    el.addEventListener('mouseleave', () => { el.style.background = bg; });
  }
  // Keep the player from seeing our clicks (pause/fullscreen toggles).
  for (const t of ['click', 'dblclick', 'mousedown', 'mouseup', 'pointerdown', 'pointerup', 'contextmenu'])
    host.addEventListener(t, e => e.stopPropagation());
  host.addEventListener('mouseenter', () => clearTimeout(hideTimer));
  host.addEventListener('mouseleave', () => scheduleHide());

  main.addEventListener('click', () => { menuOpen ? closeMenu() : openMenu(); });
  close.addEventListener('click', () => { if (current) dismissed.add(current); hide(); });

  function icon() {
    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('width', '14'); svg.setAttribute('height', '14'); svg.setAttribute('viewBox', '0 0 16 16');
    const p = document.createElementNS(ns, 'path');
    p.setAttribute('d', 'M7 1h2v7.6l2.6-2.6 1.4 1.4L8 12.4 3 7.4 4.4 6 7 8.6zM2 13h12v2H2z');
    p.setAttribute('fill', '#fff');
    svg.append(p);
    return svg;
  }

  // ---- menu -------------------------------------------------------------------

  function row(label, detail, size, onClick) {
    const r = make('div', { display: 'flex', gap: '10px', alignItems: 'baseline', padding: '4px 12px 4px 10px', cursor: onClick ? 'pointer' : 'default', whiteSpace: 'nowrap' });
    r.append(make('span', { fontWeight: onClick ? '600' : '400', flex: '0 0 auto' }, label));
    if (detail) r.append(make('span', { color: '#666', flex: '1 1 auto', overflow: 'hidden', textOverflow: 'ellipsis' }, detail));
    if (size) r.append(make('span', { color: '#444', marginLeft: 'auto' }, size));
    if (onClick) {
      r.addEventListener('mouseenter', () => { r.style.background = '#cce4f7'; });
      r.addEventListener('mouseleave', () => { r.style.background = ''; });
      r.addEventListener('click', onClick);
    }
    return r;
  }

  function note(text, color) {
    return make('div', { padding: '5px 12px', color: color || '#555', whiteSpace: 'normal', maxWidth: '330px' }, text);
  }

  function getFormats(url, retry) {
    if (retry) formats.delete(url);
    if (!formats.has(url)) {
      const p = send({ type: 'video-formats', url, pageUrl: location.href });
      formats.set(url, p);
      p.then(r => { if (!r || !r.ok) formats.delete(url); }); // let a later click try again
    }
    return formats.get(url);
  }

  async function openMenu(retry) {
    if (!current) return;
    menuOpen = true;
    clearTimeout(hideTimer);
    const video = current, url = pageUrl();
    const direct = /^https?:/.test(video.currentSrc || '') ? video.currentSrc : '';
    menu.replaceChildren(note('Getting available qualities…'));
    menu.style.display = 'block';

    const res = await getFormats(url, retry);
    if (!menuOpen || current !== video) return;
    menu.replaceChildren();
    if (res && res.ok && res.options && res.options.length) {
      if (res.title) menu.append(make('div', { padding: '3px 12px 5px', color: '#555', borderBottom: '1px solid #e3e3e3', marginBottom: '2px', maxWidth: '330px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }, res.title));
      for (const o of res.options)
        menu.append(row(o.label, o.detail, o.size, () => download({ type: 'video-download', url, format: o.id, title: res.title, pageUrl: location.href })));
    } else {
      menu.append(note((res && res.error) || 'Light Downloader is not running. Start it, then try again.', '#a4262c'));
      menu.append(row('Try again', '', '', () => openMenu(true)));
    }
    if (direct)
      menu.append(row('Video file', 'download the player\'s source as is', '', () =>
        download({ type: 'video-download', direct: true, url: direct, filename: '', pageUrl: location.href })));
  }

  async function download(msg) {
    menu.replaceChildren(note('Sending to Light Downloader…'));
    const r = await send(msg);
    menu.replaceChildren(r && r.ok
      ? note('Added to Light Downloader.', '#107c10')
      : note((r && r.error) || 'Light Downloader is not running.', '#a4262c'));
    setTimeout(closeMenu, r && r.ok ? 1200 : 3000);
  }

  function closeMenu() {
    menuOpen = false;
    menu.style.display = 'none';
    scheduleHide();
  }

  // ---- show / hide / follow the video --------------------------------------

  async function show(video, byMouse) {
    if (!pageAllowsButton() || !eligible(video) || !(await enabled())) return;
    if (current !== video) { closeMenu(); current = video; }
    const parent = document.fullscreenElement && document.fullscreenElement !== video
      ? document.fullscreenElement : document.documentElement;
    if (document.fullscreenElement === video) return; // the bare <video> is fullscreen: nothing can sit on top
    if (host.parentNode !== parent) parent.appendChild(host);
    host.style.display = 'block';
    place();
    if (!rafId) rafId = requestAnimationFrame(follow);
    // Pointing at the video shows interest: fetch the qualities now so the menu opens instantly.
    // (Not on autoplay alone — that would run yt-dlp for every muted video ad.)
    if (byMouse) getFormats(pageUrl());
    scheduleHide();
  }

  function hide() {
    host.style.display = 'none';
    menuOpen = false;
    menu.style.display = 'none';
    cancelAnimationFrame(rafId);
    rafId = 0;
  }

  function scheduleHide() {
    clearTimeout(hideTimer);
    hideTimer = setTimeout(() => { if (!menuOpen) hide(); }, HIDE_AFTER);
  }

  function place() {
    if (!current || !current.isConnected) { hide(); return; }
    const r = current.getBoundingClientRect();
    if (r.bottom < 0 || r.top > innerHeight) { host.style.display = 'none'; return; }
    host.style.display = 'block';
    const viewW = document.documentElement.clientWidth || innerWidth; // excludes the scrollbar, like fixed positioning
    const right = Math.min(r.right, viewW);
    host.style.right = Math.max(4, Math.min(viewW - right + 10, viewW - (bar.offsetWidth || 170) - 4)) + 'px';
    host.style.top = Math.max(4, r.top + 10) + 'px';
  }

  function follow() {
    rafId = 0;
    if (host.style.display === 'none' && !menuOpen) return;
    place();
    rafId = requestAnimationFrame(follow);
  }

  // A video starting to play shows the button briefly; moving the mouse over a playing video shows it again.
  document.addEventListener('playing', e => { if (e.target instanceof HTMLVideoElement) show(e.target); }, true);

  let moveQueued = false;
  document.addEventListener('mousemove', e => {
    if (moveQueued) return;
    moveQueued = true;
    setTimeout(() => {
      moveQueued = false;
      for (const v of document.getElementsByTagName('video')) {
        const r = v.getBoundingClientRect();
        if (e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom && eligible(v)) {
          show(v, true);
          return;
        }
      }
    }, 120);
  }, { capture: true, passive: true });

  document.addEventListener('fullscreenchange', () => { if (current && host.style.display !== 'none') show(current, true); });
  document.addEventListener('keydown', e => { if (e.key === 'Escape' && menuOpen) closeMenu(); }, true);
  document.addEventListener('mousedown', e => { if (menuOpen && !e.composedPath().includes(host)) closeMenu(); }, true);
})();
