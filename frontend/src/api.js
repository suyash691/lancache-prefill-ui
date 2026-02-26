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
    setToken(null);
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
  return `<img class="game-thumb" src="https://cdn.cloudflare.steamstatic.com/steam/apps/${appId}/capsule_231x87.jpg" loading="lazy" onerror="this.style.display='none'">`;
}