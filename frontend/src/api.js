import { t } from './i18n/i18n.js';
import { showToast } from './ui/toast.js';
import { showLogin } from './auth/login.js';

let token = sessionStorage.getItem('st');

export function getToken() { return token; }

export function setToken(val) {
  token = val;
  if (val) sessionStorage.setItem('st', val);
  else sessionStorage.removeItem('st');
}

export async function api(path, opts = {}) {
  opts.headers = { ...opts.headers || {} };
  if (token) opts.headers['X-Session-Token'] = token;
  if (opts.body) opts.headers['Content-Type'] = 'application/json';

  const r = await fetch(path, opts);

  if (r.status === 401 && !path.includes('/auth/')) {
    // A token that stops working means the 7-day session expired (or the
    // backend re-minted it) — say so instead of a bare login popup.
    const hadToken = !!token;
    setToken(null);
    if (hadToken) showToast(t('login.sessionExpired'), 'error');
    showLogin();
    throw new Error('unauthorized');
  }
  if (r.status === 409) {
    showToast(t('toast.jobRunning'), 'error');
    throw new Error('conflict');
  }
  if (!r.ok) {
    showToast(t('toast.requestFailed', r.status), 'error');
    throw new Error(`HTTP ${r.status}`);
  }

  const ct = r.headers.get('content-type');
  return ct && ct.includes('json') ? r.json() : null;
}

// Escape untrusted text (e.g. Steam-supplied game names, error messages)
// before interpolating into innerHTML. Prevents DOM XSS.
export function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, c =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

export function fmtB(b) {
  if (b > 1e9) return (b / 1e9).toFixed(1) + ' GB';
  if (b > 1e6) return (b / 1e6).toFixed(1) + ' MB';
  return (b / 1e3).toFixed(0) + ' KB';
}

export function timeAgo(date) {
  const s = Math.floor((Date.now() - date) / 1000);
  if (s < 60) return 'just now';
  if (s < 3600) return Math.floor(s / 60) + 'm ago';
  if (s < 86400) return Math.floor(s / 3600) + 'h ago';
  return Math.floor(s / 86400) + 'd ago';
}

export function progressBar(pct, type = 'prefill') {
  return `<div class="panel-progress"><div class="panel-progress-bar ${type}-bar" style="width:${pct}%"></div></div>`;
}

export function steamThumb(appId) {
  // Legacy CDN path first (covers most titles with no backend hop). Titles
  // released since ~2024 only serve capsules from content-hashed URLs that
  // cannot be constructed client-side — /api/thumb resolves those via the
  // store API. Hide the image only when both sources miss.
  return `<img class="game-thumb" src="https://cdn.cloudflare.steamstatic.com/steam/apps/${appId}/capsule_231x87.jpg" loading="lazy" onerror="if(!this.dataset.fb){this.dataset.fb=1;this.src='/api/thumb/${appId}';}else{this.style.display='none';}">`;
}