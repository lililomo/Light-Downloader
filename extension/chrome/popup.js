const api = globalThis.browser || globalThis.chrome;

document.addEventListener('DOMContentLoaded', async () => {
  const cb = document.getElementById('enabled');
  try { const { enabled } = await api.storage.local.get({ enabled: true }); cb.checked = enabled !== false; }
  catch (e) { cb.checked = true; }
  cb.addEventListener('change', () => { try { api.storage.local.set({ enabled: cb.checked }); } catch (e) { } });

  const box = document.getElementById('status');
  const text = document.getElementById('statusText');
  const hint = document.getElementById('hint');
  let s = null;
  try { s = await api.runtime.sendMessage({ type: 'status' }); } catch (e) { }
  if (s && s.running) {
    box.className = 'status ok';
    text.textContent = `Connected to the desktop app (port ${s.port})`;
    if (!s.capture) {
      hint.hidden = false;
      hint.textContent = 'Capturing is turned off for this browser in Light Downloader → Options → General.';
    }
  } else {
    box.className = 'status bad';
    text.textContent = 'Light Downloader is not running';
    hint.hidden = false;
    hint.textContent = 'Start the desktop app; until then the browser downloads files itself.';
  }
});
