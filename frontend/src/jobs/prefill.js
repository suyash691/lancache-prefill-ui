import { state } from '../state.js';
import { api, fmtB, progressBar } from '../api.js';
import { showToast } from '../ui/toast.js';
import { t } from '../i18n/i18n.js';
import { loadApps } from '../pages/selected.js';

export async function prefill(force) {
  const btn = document.getElementById(force ? 'bForce' : 'bSync'); btn.classList.add('btn-loading');
  try { await api('/api/prefill', { method: 'POST', body: JSON.stringify({ force, appIds: null }) }); showToast(t('toast.prefillStarted'), 'success'); }
  catch {} finally { btn.classList.remove('btn-loading'); }
}

export async function cancelJob() {
  const btn = document.getElementById('bCancel'); btn.classList.add('btn-loading');
  try { await api('/api/cancel', { method: 'POST' }); showToast(t('toast.cancelling'), 'info'); }
  catch {} finally { btn.classList.remove('btn-loading'); }
}

export async function dequeueSync(appId) { try { await api(`/api/prefill/queue/${appId}`, { method: 'DELETE' }); } catch {} }

function statusIcon(status) {
  switch (status) {
    case 'cached': return '✅';
    case 'partial': return '⚠️';
    case 'failed': return '❌';
    case 'skipped': return '⚪';
    case 'no_depots': return '⚪';
    default: return '⏳';
  }
}

function renderAppResults(results) {
  if (!results || !results.length) return '';
  return results.map(r => {
    const icon = statusIcon(r.status);
    const chunkInfo = r.chunksTotal > 0 ? ` — ${r.chunksOk.toLocaleString()}/${r.chunksTotal.toLocaleString()} chunks` : '';
    const sizeInfo = r.bytes > 0 ? ` — ${fmtB(r.bytes)}` : '';
    const hasIssues = (r.warnings && r.warnings.length > 0) || (r.errors && r.errors.length > 0);
    
    let html = `<div class="scan-row" style="padding:4px 0">`;
    html += `<div class="scan-row-flex"><span>${icon} ${r.name}${chunkInfo}${sizeInfo}</span>`;
    if (hasIssues) html += `<button class="btn btn-b btn-s prefill-toggle">▼</button>`;
    html += `</div>`;
    
    if (hasIssues) {
      html += `<div style="display:none;padding:4px 0 4px 20px;font-size:11px;color:var(--muted)">`;
      if (r.errors) r.errors.forEach(e => html += `<div style="color:var(--red)">✕ ${e}</div>`);
      if (r.warnings) r.warnings.forEach(w => html += `<div style="color:var(--yellow)">⚠ ${w}</div>`);
      html += `</div>`;
    }
    html += `</div>`;
    return html;
  }).join('');
}

function attachToggleListeners(container) {
  container.querySelectorAll('.prefill-toggle').forEach(btn => {
    btn.addEventListener('click', (e) => {
      e.stopPropagation();
      const details = btn.closest('.scan-row').querySelector('[style*="display:none"], [style*="display: none"], [style*="display:block"], [style*="display: block"]');
      if (details) {
        const visible = details.style.display !== 'none';
        details.style.display = visible ? 'none' : 'block';
        btn.textContent = visible ? '▼' : '▲';
      }
    });
  });
}

function savePrefillResults(p) {
  try {
    localStorage.setItem('prefillResults', JSON.stringify({
      status: p.status, results: p.results, done: p.done, total: p.total,
      bytesTransferred: p.bytesTransferred, time: new Date().toISOString()
    }));
  } catch {}
}

export function dismissPrefillPill() {
  const pill = document.getElementById('prefillPill');
  pill.style.display = 'none';
  document.getElementById('prefillPanel').style.display = 'none';
  state.prefillWasRunning = false;
  state.prefillDoneHandled = false;
  try { localStorage.removeItem('prefillResults'); } catch {}
}

export function restorePrefillPill() {
  try {
    const raw = localStorage.getItem('prefillResults');
    if (!raw) return;
    const saved = JSON.parse(raw);
    if (!saved.status || !saved.status.startsWith('done')) return;
    const pill = document.getElementById('prefillPill');
    const pillText = document.getElementById('prefillPillText');
    const body = document.getElementById('prefillPanelBody');
    pill.style.display = 'block'; pill.classList.remove('active-pill');
    pillText.innerHTML = `Prefill: ${saved.status} <button class="pill-dismiss" id="prefillDismiss">✕</button>`;
    pill.style.borderColor = 'var(--green)';
    const resultsHtml = renderAppResults(saved.results);
    body.innerHTML = `<div style="margin-bottom:8px;font-weight:600">${saved.status}</div>${resultsHtml}`;
    attachToggleListeners(body);
    document.getElementById('prefillDismiss')?.addEventListener('click', (e) => { e.stopPropagation(); dismissPrefillPill(); });
  } catch {}
}

export function updatePrefillUI(p) {
  const pill = document.getElementById('prefillPill');
  const pillText = document.getElementById('prefillPillText');
  const body = document.getElementById('prefillPanelBody');
  if (!p.running && p.status === 'idle') { pill.style.display = 'none'; return; }
  if (p.running) {
    state.prefillWasRunning = true;
    try { localStorage.removeItem('prefillResults'); } catch {}
    pill.style.display = 'block'; pill.classList.add('active-pill');
    const effectiveTotal = p.total + state.syncQueue.reduce((s, q) => s + (q.appIds?.length || 0), 0);
    const pct = effectiveTotal > 0 ? Math.round(p.done / effectiveTotal * 100) : 0;
    const chunkDetail = p.currentChunksTotal > 0 ? ` (${p.currentChunksDone}/${p.currentChunksTotal})` : '';
    pillText.textContent = `⟳ ${t('progress.prefillPct', pct)} — ${p.currentApp || t('prefill.starting')}${chunkDetail}`;
    pill.style.borderColor = 'var(--blue)';
    let queueHtml = '';
    if (state.syncQueue.length > 0) {
      queueHtml = `<div style="margin-top:8px;border-top:1px solid var(--primary);padding-top:8px"><strong>${t('prefill.queued', state.syncQueue.length)}</strong>`;
      state.syncQueue.forEach(item => {
        const ids = item.appIds || [];
        const name = ids.map(id => state.appNames[id] || `App ${id}`).join(', ');
        queueHtml += `<div class="scan-row-flex" style="padding:2px 0"><span>${name}</span><button class="btn btn-d btn-s" data-dequeue="${ids[0]}">✕</button></div>`;
      });
      queueHtml += '</div>';
    }
    // Per-app chunk progress
    let chunkHtml = '';
    if (p.currentChunksTotal > 0) {
      const chunkPct = Math.round(p.currentChunksDone / p.currentChunksTotal * 100);
      const chunkBytes = p.currentAppBytes > 0 ? ` — ${fmtB(p.currentAppBytes)}` : '';
      chunkHtml = `<div class="chunk-progress"><div class="chunk-progress-label">${p.currentChunksDone.toLocaleString()} / ${p.currentChunksTotal.toLocaleString()} chunks${chunkBytes}</div>${progressBar(chunkPct, 'chunk')}</div>`;
    }
    // Up next: pending (absorbed into backend) + queued (not yet absorbed)
    let upNextHtml = '';
    const pendingNames = p.pending || [];
    const queuedNames = state.syncQueue.flatMap(q => (q.appIds || []).map(id => state.appNames[id] || `App ${id}`));
    const allUpNext = [...pendingNames, ...queuedNames];
    if (allUpNext.length > 0) {
      upNextHtml = `<div style="margin-top:8px;border-top:1px solid var(--primary);padding-top:6px;font-size:12px;color:var(--muted)"><strong>${t('prefill.upNext')}</strong> ${allUpNext.join(', ')}</div>`;
    }
    const resultsHtml = renderAppResults(p.results);
    body.innerHTML = `${progressBar(pct, 'prefill')}<div>${p.done}/${effectiveTotal} ${t('prefill.games')}</div><div>${t('prefill.current')} ${p.currentApp || '—'}</div>${chunkHtml}<div>${t('prefill.transferred')} ${p.bytesTransferred > 0 ? fmtB(p.bytesTransferred) : '—'}</div>${upNextHtml}${resultsHtml}${queueHtml}`;
    body.querySelectorAll('[data-dequeue]').forEach(btn => btn.addEventListener('click', () => dequeueSync(parseInt(btn.dataset.dequeue))));
    attachToggleListeners(body);
  } else if (state.prefillWasRunning) {
    pill.style.display = 'block'; pill.classList.remove('active-pill');
    pillText.innerHTML = `Prefill: ${p.status} <button class="pill-dismiss" id="prefillDismiss">✕</button>`;
    pill.style.borderColor = 'var(--green)';
    const resultsHtml = renderAppResults(p.results);
    body.innerHTML = `<div style="margin-bottom:8px;font-weight:600">${p.status}</div>${resultsHtml}`;
    attachToggleListeners(body);
    document.getElementById('prefillDismiss')?.addEventListener('click', (e) => { e.stopPropagation(); dismissPrefillPill(); });
    state.lastSyncTime = new Date();
    savePrefillResults(p);
    if (p.status.startsWith('done') && !state.prefillDoneHandled) { state.prefillDoneHandled = true; loadApps(); }
  } else pill.style.display = 'none';
}

export function setBtns(job) {
  const busy = !!job;
  const fullJob = job === 'scan' || (job === 'prefill' && !state.syncQueue.length && !state.isQueuedPrefill);
  ['bSync', 'bForce', 'bScan', 'bRefresh'].forEach(b => { const e = document.getElementById(b); if (e) e.disabled = busy; });
  document.getElementById('bCancel').style.display = busy ? 'inline-block' : 'none';
  document.querySelectorAll('.tr .btn-a.btn-s, .evict-row .btn-g.btn-s').forEach(b => b.disabled = fullJob);
}