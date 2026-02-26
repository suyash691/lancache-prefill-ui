import { state } from '../state.js';
import { api, steamThumb } from '../api.js';
import { showToast } from '../ui/toast.js';
import { showConfirm } from '../ui/confirm.js';
import { updateTabCounts } from '../ui/tabs.js';
import { t, randomTip } from '../i18n/i18n.js';

const tip = randomTip();

export async function loadApps() {
  document.getElementById('rows').innerHTML = `<div class="empty"><div class="spinner"></div><div style="margin-top:8px;font-size:12px;color:var(--muted)">${tip}</div></div>`;
  try {
    const data = await api('/api/apps');
    state.apps = data.map(a => a.appId);
    state.appNames = {}; state.utd = {}; state.manifests = {}; state.cachedManifests = {}; state.appStatus = {};
    data.forEach(a => {
      state.appNames[a.appId] = a.name;
      if (a.upToDate !== null) state.utd[a.appId] = a.upToDate;
      if (a.latestManifest) state.manifests[a.appId] = a.latestManifest;
      if (a.cachedManifest) state.cachedManifests[a.appId] = a.cachedManifest;
      if (a.status) state.appStatus[a.appId] = a.status;
    });
    updateTabCounts(); updateSyncButton(); renderApps();
    document.getElementById('infoManifests').textContent = new Date().toLocaleTimeString();
  } catch { document.getElementById('rows').innerHTML = `<div class="empty">${t('selected.failedLoad')}</div>`; }
}

function updateSyncButton() {
  const n = Object.values(state.utd).filter(v => v === false).length;
  document.getElementById('bSync').textContent = n > 0 ? `⟳ ${t('actions.syncN', n)}` : `⟳ ${t('actions.sync')}`;
}

export function renderApps() {
  const el = document.getElementById('rows');
  if (!state.apps.length) {
    el.innerHTML = `<div class="empty">${t('selected.empty', `<span class="link-action" data-action="switchTab" data-tab="library">${t('selected.emptyLink')}</span>`)}</div>`;
    el.querySelector('[data-action="switchTab"]')?.addEventListener('click', () => {
      import('../ui/tabs.js').then(m => m.switchTab('library'));
    });
    return;
  }
  const sortBy = document.getElementById('sortSelected')?.value || 'name';
  const sorted = [...state.apps].sort((a, b) => {
    if (sortBy === 'id') return a - b;
    if (sortBy === 'status') { const sa = state.utd[a] === false ? 0 : state.utd[a] === true ? 2 : 1, sb = state.utd[b] === false ? 0 : state.utd[b] === true ? 2 : 1; return sa - sb; }
    return (state.appNames[a] || '').localeCompare(state.appNames[b] || '');
  });
  el.innerHTML = sorted.map(id => {
    const sc = state.scanMap[id], name = state.appNames[id] || `App ${id}`, cm = state.cachedManifests[id] || '';
    const thumb = steamThumb(id);
    const appSt = state.appStatus[id];
    let cache = `<span class="badge b">—</span>`;
    if (appSt === 'partial') { cache = `<span class="badge y" title="Partially cached">${t('badge.partial')}</span>`; }
    else if (sc) { cache = sc.cached ? `<span class="badge g"${cm ? ` title="Cached: ${cm}"` : ''}>${t('badge.cached')}</span>` : sc.error ? '<span class="badge b">—</span>' : `<span class="badge r">${t('badge.missing')}</span>`; }
    else if (cm) { cache = `<span class="badge g" title="Cached: ${cm}">${t('badge.cached')}</span>`; }
    const u = state.utd[id], mf = state.manifests[id] || '';
    const ver = u === true ? `<span class="badge g" title="Latest: ${mf}">${t('badge.current')}</span>` : u === false ? `<span class="badge y" title="Latest: ${mf}">${t('badge.updateAvailable')}</span>` : '<span class="badge b">—</span>';
    const needsSync = u === false || (!sc?.cached && u !== true) || appSt === 'partial';
    const actionBtn = needsSync
      ? `<button class="btn btn-a btn-s" data-action="prefillOne" data-id="${id}">⟳ ${t('actions.sync')}</button>`
      : `<button class="btn btn-b btn-s" data-action="checkUpdate" data-id="${id}">${t('actions.check')}</button>`;
    return `<div class="tr" data-appid="${id}"><span class="col-id"><a href="https://store.steampowered.com/app/${id}" target="_blank">${id}</a></span><span class="game-name">${thumb}<span>${name}</span></span><span class="col-cache">${cache}</span><span class="col-update">${ver}</span><span>${actionBtn} <button class="btn btn-d btn-s" data-action="rmApp" data-id="${id}">✕</button></span></div>`;
  }).join('');
  // Attach event listeners
  el.querySelectorAll('[data-action="prefillOne"]').forEach(btn => btn.addEventListener('click', () => prefillOne(btn, parseInt(btn.dataset.id))));
  el.querySelectorAll('[data-action="checkUpdate"]').forEach(btn => btn.addEventListener('click', () => checkUpdate(btn, parseInt(btn.dataset.id))));
  el.querySelectorAll('[data-action="rmApp"]').forEach(btn => btn.addEventListener('click', () => rmApp(btn, parseInt(btn.dataset.id))));
}

export async function addById() {
  const input = document.getElementById('addAppId');
  const id = parseInt(input.value);
  if (!id || id < 1) { showToast(t('toast.invalidAppId'), 'error'); return; }
  try { await api('/api/apps/add', { method: 'POST', body: JSON.stringify({ appId: id }) }); input.value = ''; showToast(t('toast.appAdded', id), 'success'); loadApps(); } catch {}
}

async function rmApp(btn, id) {
  const name = state.appNames[id] || `App ${id}`;
  if (!await showConfirm(t('confirm.removeGame'), t('confirm.removeGameMsg', name))) return;
  btn.classList.add('btn-loading');
  try {
    await api(`/api/apps/${id}`, { method: 'DELETE' });
    state.apps = state.apps.filter(a => a !== id);
    delete state.appNames[id]; delete state.scanMap[id]; delete state.utd[id]; delete state.manifests[id]; delete state.cachedManifests[id]; delete state.appStatus[id];
    const item = state.library.find(a => a.appId === id); if (item) item.selected = false;
    updateTabCounts(); renderApps();
  } catch { showToast(t('toast.failedRemove'), 'error'); }
}

async function checkUpdate(btn, id) {
  btn.classList.add('btn-loading');
  try {
    const r = await api(`/api/apps/${id}/check`, { method: 'POST' });
    if (r.upToDate !== null) state.utd[id] = r.upToDate;
    renderApps(); updateSyncButton();
    showToast(r.upToDate === false ? t('toast.updateAvailable') : t('toast.upToDate'), 'info');
  } catch {} finally { btn.classList.remove('btn-loading'); }
}

async function prefillOne(btn, id) {
  btn.classList.add('btn-loading');
  try { await api('/api/prefill', { method: 'POST', body: JSON.stringify({ force: true, appIds: [id] }) }); showToast(t('toast.syncQueued'), 'success'); }
  catch {} finally { btn.classList.remove('btn-loading'); }
}

export async function refreshManifests() {
  const btn = document.getElementById('bRefresh'); btn.classList.add('btn-loading');
  try { await api('/api/apps/refresh', { method: 'POST' }); showToast(t('toast.manifestsRefreshed'), 'success'); loadApps(); }
  catch {} finally { btn.classList.remove('btn-loading'); }
}

export async function addFromScan(id) { await api('/api/apps/add', { method: 'POST', body: JSON.stringify({ appId: id }) }); loadApps(); }

export { updateSyncButton };