import { state } from '../state.js';
import { api, progressBar } from '../api.js';
import { showToast } from '../ui/toast.js';
import { t } from '../i18n/i18n.js';
import { renderApps, addFromScan } from '../pages/selected.js';

export async function startScan() {
  try {
    const resp = await api('/api/scan/has-previous');
    const dialog = document.getElementById('scanDialog');
    const quickBtn = document.querySelector('#scanDialog .btn-a');
    const deepBtn = document.querySelector('#scanDialog .btn-b');
    if (!resp.hasPrevious) {
      quickBtn.style.display = 'none';
      deepBtn.innerHTML = `${t('scan.initialDeep')}<br><span class="dialog-sub">${t('scan.initialDesc')}</span>`;
    } else {
      quickBtn.style.display = 'block';
      deepBtn.innerHTML = `${t('scan.fullRescan')}<br><span class="dialog-sub">${t('scan.fullDesc')}</span>`;
    }
    dialog.style.display = 'flex';
  } catch {}
}

export async function doScan(deep) {
  document.getElementById('scanDialog').style.display = 'none';
  const btn = document.getElementById('bScan'); btn.classList.add('btn-loading');
  try { await api('/api/scan', { method: 'POST', body: JSON.stringify({ deep }) }); showToast(deep ? t('toast.deepScanStarted') : t('toast.quickScanStarted'), 'success'); }
  catch (e) { if (e.message !== 'conflict') showToast(t('toast.scanFailed'), 'error'); }
  finally { btn.classList.remove('btn-loading'); }
}

export function updateScanUI(s) {
  const pill = document.getElementById('scanPill');
  const pillText = document.getElementById('scanPillText');
  const r = s.results || [];
  const prev = JSON.stringify(Object.keys(state.scanMap).sort());
  state.scanMap = {}; r.forEach(x => state.scanMap[x.appId] = x);
  const curr = JSON.stringify(Object.keys(state.scanMap).sort());
  if (s.running) {
    state.scanWasRunning = true; pill.style.display = 'block'; pill.classList.add('active-pill');
    const pct = s.total > 0 ? Math.round(s.done / s.total * 100) : 0;
    const status = s.status || '';
    if (status.includes('Scanning cache') || status.includes('Reading') || status.includes('Clearing') || status.includes('Processing') || status.includes('Loading') || status.includes('Resolving'))
      pillText.textContent = `🔍 ${t('progress.indexing')}`;
    else if (status.includes('Checking') || status.startsWith('['))
      pillText.textContent = `🔍 ${t('progress.scanPct', pct, s.done, s.total)}`;
    else pillText.textContent = `🔍 Scan: ${status}`;
    pill.style.borderColor = 'var(--yellow)';
  } else if (state.scanWasRunning) {
    pill.style.display = 'block'; pill.classList.remove('active-pill');
    const cached = r.filter(x => x.cached).length;
    const notCached = r.filter(x => !x.cached && !x.error).length;
    pillText.textContent = `Scan: ${cached} ${t('progress.cached')}, ${notCached} ${t('progress.notCached')}`;
    pill.style.borderColor = 'var(--green)';
    state.lastScanTime = new Date();
    if (!state.scanDoneHandled) { state.scanDoneHandled = true; state.cacheLoaded = false; setTimeout(() => { state.scanDoneHandled = false; state.scanWasRunning = false; pill.style.display = 'none'; }, 15000); }
  } else pill.style.display = 'none';
  if (prev !== curr) renderApps();
  updateScanPanel(s);
}

function updateScanPanel(s) {
  const body = document.getElementById('scanPanelBody');
  if (s.running && (!s.results || !s.results.length)) {
    const pct = s.total > 0 ? Math.round(s.done / s.total * 100) : 0;
    body.innerHTML = `${progressBar(pct, 'scan')}<div style="color:var(--muted)">${s.status || 'Starting...'}</div>`;
    return;
  }
  if (!s.results || !s.results.length) { body.innerHTML = ''; return; }
  const r = s.results;
  const discovered = r.filter(x => x.cached && !x.selected);
  const inCache = r.filter(x => x.cached && x.selected);
  const partial = r.filter(x => !x.cached && !x.error && x.selected && state.appStatus[x.appId] === 'partial');
  const missing = r.filter(x => !x.cached && !x.error && x.selected && state.appStatus[x.appId] !== 'partial');
  let h = '';
  if (s.running) { const pct = s.total > 0 ? Math.round(s.done / s.total * 100) : 0; h = `${progressBar(pct, 'scan')}<div style="color:var(--muted);margin-bottom:8px">${s.status}</div>`; }
  if (discovered.length) {
    h += `<div class="scan-group" style="color:var(--yellow)">${t('progress.discoveredInCache', discovered.length)}</div>`;
    h += discovered.map(x => `<div class="scan-row scan-row-flex"><span>${x.name}</span><button class="btn btn-g btn-s" data-action="addFromScan" data-id="${x.appId}">+ ${t('actions.add')}</button></div>`).join('');
  }
  if (inCache.length) {
    h += `<div class="scan-group" style="color:var(--green)">${t('progress.selectedCached', inCache.length)}</div>`;
    h += inCache.map(x => `<div class="scan-row">${x.name}</div>`).join('');
  }
  if (partial.length) {
    h += `<div class="scan-group" style="color:var(--yellow)">${t('progress.partiallyCached', partial.length)}</div>`;
    h += partial.map(x => `<div class="scan-row">${x.name}</div>`).join('');
  }
  if (missing.length) {
    h += `<div class="scan-group" style="color:var(--red)">${t('progress.notInCache', missing.length)}</div>`;
    h += missing.map(x => `<div class="scan-row">${x.name}</div>`).join('');
  }
  body.innerHTML = h || 'No results';
  body.querySelectorAll('[data-action="addFromScan"]').forEach(btn => btn.addEventListener('click', () => addFromScan(parseInt(btn.dataset.id))));
}