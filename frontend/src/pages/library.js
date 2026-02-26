import { state } from '../state.js';
import { api, steamThumb } from '../api.js';
import { showToast } from '../ui/toast.js';
import { showConfirm } from '../ui/confirm.js';
import { updateTabCounts } from '../ui/tabs.js';
import { t } from '../i18n/i18n.js';
import { loadApps } from './selected.js';

let _timer = null;

export async function loadLibrary() {
  document.getElementById('libRows').innerHTML = '<div class="empty"><div class="spinner"></div></div>';
  try { state.library = await api('/api/library'); state.libPage = 1; updateTabCounts(); renderLibrary(); }
  catch { document.getElementById('libRows').innerHTML = `<div class="empty">${t('selected.failedLoad')}</div>`; }
}

export function filterLibrary() { clearTimeout(_timer); _timer = setTimeout(() => { state.libPage = 1; renderLibrary(); }, 150); }

export function renderLibrary() {
  const el = document.getElementById('libRows');
  const q = (document.getElementById('libSearch').value || '').toLowerCase();
  const filtered = q ? state.library.filter(a => a.name.toLowerCase().includes(q) || String(a.appId).includes(q)) : state.library;
  if (!filtered.length) { el.innerHTML = `<div class="empty">${q ? t('library.noMatches') : t('library.noGames')}</div>`; document.getElementById('libLoadMore').style.display = 'none'; return; }
  const visible = filtered.slice(0, state.libPage * state.libPageSize);
  el.innerHTML = visible.map(a => {
    const sel = a.selected, thumb = steamThumb(a.appId);
    return `<div class="lib-row${sel ? ' lib-selected' : ''}"><span class="col-id">${a.appId}</span><span class="game-name">${thumb}<span>${a.name}${sel ? ` <span class="badge g">${t('badge.selected')}</span>` : ''}</span></span><span>${sel ? `<button class="btn btn-d btn-s" data-action="toggle" data-id="${a.appId}" data-add="false">${t('actions.remove')}</button>` : `<button class="btn btn-g btn-s" data-action="toggle" data-id="${a.appId}" data-add="true">+ ${t('actions.add')}</button>`}</span></div>`;
  }).join('');
  el.querySelectorAll('[data-action="toggle"]').forEach(btn => btn.addEventListener('click', () => toggleApp(btn, parseInt(btn.dataset.id), btn.dataset.add === 'true')));
  const remaining = filtered.length - visible.length;
  const more = document.getElementById('libLoadMore');
  if (remaining > 0) { more.style.display = 'block'; document.getElementById('libRemaining').textContent = t('library.remaining', remaining); }
  else more.style.display = 'none';
}

export function showMoreLibrary() { state.libPage++; renderLibrary(); }

export async function selectFiltered() {
  const q = (document.getElementById('libSearch').value || '').toLowerCase();
  if (!q) { showToast(t('toast.filterFirst'), 'info'); return; }
  const filtered = state.library.filter(a => !a.selected && (a.name.toLowerCase().includes(q) || String(a.appId).includes(q)));
  if (!filtered.length) { showToast(t('toast.noUnselected'), 'info'); return; }
  if (!await showConfirm(t('confirm.addFiltered'), t('confirm.addFilteredMsg', filtered.length, q))) return;
  for (const a of filtered.slice(0, 50)) {
    try { await api('/api/apps/add', { method: 'POST', body: JSON.stringify({ appId: a.appId }) }); a.selected = true; } catch { break; }
  }
  renderLibrary(); loadApps(); showToast(t('toast.addedN', Math.min(filtered.length, 50)), 'success');
}

async function toggleApp(btn, id, add) {
  btn.classList.add('btn-loading');
  try {
    if (add) await api('/api/apps/add', { method: 'POST', body: JSON.stringify({ appId: id }) });
    else await api(`/api/apps/${id}`, { method: 'DELETE' });
    const item = state.library.find(a => a.appId === id); if (item) item.selected = add;
    renderLibrary(); loadApps();
  } catch { showToast(t('toast.failed'), 'error'); }
}