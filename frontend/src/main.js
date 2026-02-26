import './styles/index.css';
import { state } from './state.js';
import { setToken } from './api.js';
import { initTheme, toggleTheme } from './ui/theme.js';
import { initInfoPopover, toggleInfoPopover } from './ui/info.js';
import { closeConfirm } from './ui/confirm.js';
import { showLogin, doLogin, submitCode, showStep } from './auth/login.js';
import { doLogout } from './auth/logout.js';
import { loadApps, refreshManifests, addById, renderApps } from './pages/selected.js';
import { loadLibrary, filterLibrary, showMoreLibrary, selectFiltered } from './pages/library.js';
import { startSSE } from './jobs/sse.js';
import { restorePrefillPill, prefill, cancelJob } from './jobs/prefill.js';
import { startScan, doScan } from './jobs/scan.js';
import { switchTab } from './ui/tabs.js';
import { randomTip } from './i18n/i18n.js';

// Initialize theme
initTheme();
initInfoPopover();

// Show a loading tip immediately
const rowsEl = document.getElementById('rows');
if (rowsEl) rowsEl.innerHTML = `<div class="empty"><div class="spinner"></div><div style="margin-top:8px;font-size:12px;color:var(--muted)">${randomTip()}</div></div>`;

// Keyboard shortcuts
document.addEventListener('keydown', e => {
  if (e.key === 'Escape') {
    document.getElementById('scanDialog').style.display = 'none';
    document.getElementById('infoPopover').style.display = 'none';
    closeConfirm();
  }
});

// Main init
async function init() {
  // Lancache detection
  try {
    const lc = await (await fetch('/api/lancache')).json();
    if (lc.detected) {
      state.lancacheIp = lc.ip;
      document.getElementById('infoBtn').style.display = 'inline-flex';
      document.getElementById('infoLancacheIp').textContent = lc.ip;
    } else {
      document.getElementById('lancacheWarning').style.display = 'block';
    }
  } catch {}

  // Auto-login
  try {
    const r = await fetch('/api/auth/auto-login', { method: 'POST' });
    const d = await r.json();
    if (d.success) {
      setToken(d.sessionToken);
      onLoggedIn();
      return;
    }
  } catch {}

  setToken(null);
  document.getElementById('rows').innerHTML = '';
  showLogin();
}

function onLoggedIn() {
  document.getElementById('logoutBtn').style.display = 'inline-flex';
  loadApps();
  loadLibrary();
  startSSE();
  restorePrefillPill();
}

// Expose to global for HTML onclick handlers
window._app = {
  onLoggedIn,
  switchTab,
  toggleTheme,
  toggleInfoPopover,
  closeConfirm,
  doLogin: () => doLogin(onLoggedIn),
  submitCode: () => submitCode(onLoggedIn),
  showStep,
  doLogout,
  prefill,
  cancelJob,
  startScan,
  doScan,
  refreshManifests,
  addById,
  renderApps,
  filterLibrary,
  showMoreLibrary,
  selectFiltered,
  filterCacheBrowser: () => import('./pages/cache-browser.js').then(m => m.filterCacheBrowser()),
  saveSettings: () => import('./pages/settings.js').then(m => m.saveSettings()),
  reconcileCache: () => import('./pages/settings.js').then(m => m.reconcileCache()),
};

init();