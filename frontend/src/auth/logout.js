import { api, setToken } from '../api.js';
import { showConfirm } from '../ui/confirm.js';
import { showLogin } from './login.js';
import { t } from '../i18n/i18n.js';

export async function doLogout() {
  if (!await showConfirm(t('confirm.logout'), t('confirm.logoutMsg'))) return;
  try { await api('/api/auth/logout', { method: 'POST' }); } catch {}
  setToken(null);
  if (window._sse) window._sse.close();
  document.getElementById('logoutBtn').style.display = 'none';
  document.getElementById('rows').innerHTML = '';
  showLogin();
}