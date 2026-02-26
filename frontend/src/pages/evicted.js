import { state } from '../state.js';
import { api, steamThumb } from '../api.js';
import { showToast } from '../ui/toast.js';
import { showConfirm } from '../ui/confirm.js';
import { updateTabCounts } from '../ui/tabs.js';
import { t } from '../i18n/i18n.js';
import { loadApps } from './selected.js';

export async function loadEvicted() {
  if (!state.evictedLoaded) document.getElementById('evictedRows').innerHTML = '<div class="empty"><div class="spinner"></div></div>';
  try {
    const list = await api('/api/evicted');
    state.evictedCount = list.length; state.evictedLoaded = true; updateTabCounts();
    const el = document.getElementById('evictedRows');
    if (!list.length) { el.innerHTML = `<div class="empty">${t('evicted.empty')}</div>`; return; }
    el.innerHTML = list.map(a => `<div class="evict-row"><span class="col-id">${a.appId}</span><span class="game-name">${steamThumb(a.appId)}<span>${a.name}</span></span><span><button class="btn btn-g btn-s" data-action="recache" data-id="${a.appId}">⟳ ${t('actions.recache')}</button> <button class="btn btn-d btn-s" data-action="removeEvicted" data-id="${a.appId}">✕ ${t('actions.remove')}</button></span></div>`).join('');
    el.querySelectorAll('[data-action="recache"]').forEach(btn => btn.addEventListener('click', () => recacheApp(btn, parseInt(btn.dataset.id))));
    el.querySelectorAll('[data-action="removeEvicted"]').forEach(btn => btn.addEventListener('click', () => removeEvicted(btn, parseInt(btn.dataset.id))));
  } catch {}
}

async function recacheApp(btn, id) {
  btn.classList.add('btn-loading');
  try { await api(`/api/evicted/${id}/recache`, { method: 'POST' }); showToast(t('toast.recacheStarted'), 'success'); state.evictedLoaded = false; loadEvicted(); } catch {}
}

async function removeEvicted(btn, id) {
  if (!await showConfirm(t('confirm.removeEvicted'), t('confirm.removeEvictedMsg', state.appNames[id] || 'App ' + id))) return;
  btn.classList.add('btn-loading');
  try { await api(`/api/evicted/${id}`, { method: 'DELETE' }); state.evictedLoaded = false; loadEvicted(); loadApps(); } catch { showToast(t('toast.failed'), 'error'); }
}