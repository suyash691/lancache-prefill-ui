import { state } from '../state.js';
import { api, fmtB, steamThumb } from '../api.js';
import { t } from '../i18n/i18n.js';
import { loadApps } from './selected.js';

let _timer = null;

export async function loadCacheBrowser() {
  if (state.cacheLoaded) { renderCacheBrowser(); return; }
  document.getElementById('cacheRows').innerHTML = '<div class="empty"><div class="spinner"></div></div>';
  try {
    const resp = await api('/api/cache-browser');
    if (resp.message) { document.getElementById('cacheRows').innerHTML = `<div class="empty">${resp.message}</div>`; return; }
    state.cacheGames = resp.games || []; state.cacheLoaded = true;
    const totalChunks = state.cacheGames.reduce((s, g) => s + g.chunkCount, 0);
    document.getElementById('infoCacheSize').textContent = totalChunks > 0 ? `~${fmtB(totalChunks * 1048576)} (${totalChunks.toLocaleString()} ${t('cacheBrowser.chunks')})` : '—';
    renderCacheBrowser();
  } catch { document.getElementById('cacheRows').innerHTML = `<div class="empty">${t('selected.failedLoad')}</div>`; }
}

export function filterCacheBrowser() { clearTimeout(_timer); _timer = setTimeout(renderCacheBrowser, 150); }

export function renderCacheBrowser() {
  const el = document.getElementById('cacheRows');
  const q = (document.getElementById('cacheSearch').value || '').toLowerCase();
  const filtered = q ? state.cacheGames.filter(g => g.name.toLowerCase().includes(q) || String(g.appId).includes(q)) : state.cacheGames;
  if (!filtered.length) { el.innerHTML = `<div class="empty">${q ? t('cacheBrowser.noMatches') : state.cacheLoaded ? t('cacheBrowser.noCached') : t('cacheBrowser.empty')}</div>`; return; }
  el.innerHTML = filtered.slice(0, 200).map(g => {
    const size = g.chunkCount > 1000 ? (g.chunkCount / 1000).toFixed(1) + 'K' : g.chunkCount;
    const sel = g.selected, thumb = steamThumb(g.appId);
    return `<div class="cache-row"><span class="col-id"><a href="https://store.steampowered.com/app/${g.appId}" target="_blank">${g.appId}</a></span><span class="game-name">${thumb}<span>${g.name}${sel ? ` <span class="badge g">${t('badge.selected')}</span>` : ''}</span></span><span class="col-chunks">${size} ${t('cacheBrowser.chunks')}</span><span>${sel ? '' : `<button class="btn btn-g btn-s" data-action="addFromCache" data-id="${g.appId}">+ ${t('actions.add')}</button>`}</span></div>`;
  }).join('') + (filtered.length > 200 ? `<div class="empty">${t('cacheBrowser.remaining', filtered.length - 200)}</div>` : '');
  el.querySelectorAll('[data-action="addFromCache"]').forEach(btn => btn.addEventListener('click', () => addFromCache(parseInt(btn.dataset.id))));
  document.querySelectorAll('.tab')[3].innerHTML = `${t('nav.cacheBrowser')}<span class="tab-count">(${state.cacheGames.length})</span>`;
}

async function addFromCache(id) {
  await api('/api/apps/add', { method: 'POST', body: JSON.stringify({ appId: id }) });
  const g = state.cacheGames.find(x => x.appId === id); if (g) g.selected = true;
  renderCacheBrowser(); loadApps();
}