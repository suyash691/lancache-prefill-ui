import { api, esc, fmtB } from '../api.js';
import { t } from '../i18n/i18n.js';

function fmtDuration(startIso, endIso) {
  const s = Math.max(0, Math.round((new Date(endIso) - new Date(startIso)) / 1000));
  if (s < 60) return `${s}s`;
  if (s < 3600) return `${Math.floor(s / 60)}m ${s % 60}s`;
  return `${Math.floor(s / 3600)}h ${Math.floor((s % 3600) / 60)}m`;
}

const statusIcon = { cached: '✓', partial: '◐', skipped: '−', no_depots: '−', failed: '✗' };

export async function loadHistory() {
  const el = document.getElementById('historyRows');
  el.innerHTML = '<div class="empty"><div class="spinner"></div></div>';
  let runs;
  try { runs = await api('/api/history'); }
  catch { el.innerHTML = `<div class="empty">${t('selected.failedLoad')}</div>`; return; }

  if (!runs.length) {
    el.innerHTML = '<div class="empty">No prefill runs recorded yet.</div>';
    return;
  }

  el.innerHTML = runs.map(r => {
    const started = new Date(r.startedAt).toLocaleString();
    const trig = `<span class="badge ${r.trigger === 'scheduled' ? 'g' : 'b'}">${esc(r.trigger)}</span>`;
    const apps = `<span title="cached">✓${r.appsCached}</span> <span title="partial">◐${r.appsPartial}</span> `
      + `<span title="up to date / skipped">−${r.appsSkipped}</span> <span title="failed">✗${r.appsFailed}</span>`;
    const status = r.status === 'done'
      ? `<span class="badge g">done</span>` : `<span class="badge y">cancelled</span>`;
    const details = (r.results || []).map(a => {
      const sz = [a.bytes ? fmtB(a.bytes) : null, a.cachedBytes ? `${fmtB(a.cachedBytes)} cached` : null]
        .filter(Boolean).join(' + ');
      return `<div class="history-app"><span>${statusIcon[a.status] || '?'}</span> ${esc(a.name)}`
        + `${sz ? ` <span class="size-hint">${sz}</span>` : ''}</div>`;
    }).join('');
    return `<div class="tr tr-history" data-runid="${r.id}">`
      + `<span>${started}</span><span>${trig}</span><span>${fmtDuration(r.startedAt, r.finishedAt)}</span>`
      + `<span>${apps}</span><span>${r.bytes ? fmtB(r.bytes) : '—'}</span><span>${status}</span></div>`
      + (details ? `<div class="history-details" id="hd-${r.id}" style="display:none">${details}</div>` : '');
  }).join('');

  el.querySelectorAll('.tr-history').forEach(row => row.addEventListener('click', () => {
    const d = document.getElementById(`hd-${row.dataset.runid}`);
    if (d) d.style.display = d.style.display === 'none' ? 'block' : 'none';
  }));
}
