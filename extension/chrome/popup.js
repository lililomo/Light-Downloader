const PORTS = [7414, 7415, 7416, 7417];

async function ping() {
  for (const p of PORTS) {
    try { const r = await fetch(`http://127.0.0.1:${p}/ping`); if (r.ok) return p; } catch (e) { }
  }
  return null;
}

document.addEventListener('DOMContentLoaded', async () => {
  const cb = document.getElementById('enabled');
  try { const { enabled } = await chrome.storage.local.get({ enabled: true }); cb.checked = enabled; }
  catch (e) { cb.checked = true; }
  cb.addEventListener('change', () => { try { chrome.storage.local.set({ enabled: cb.checked }); } catch (e) { } });

  const el = document.getElementById('status');
  const port = await ping();
  if (port) { el.textContent = `Connected · port ${port}`; el.className = 'status ok'; }
  else { el.textContent = 'Desktop app not running'; el.className = 'status bad'; }
});
