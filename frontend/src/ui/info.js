import { state } from '../state.js';
import { api, timeAgo, fmtB } from '../api.js';
import { t } from '../i18n/i18n.js';

export function toggleInfoPopover() {
  const p = document.getElementById('infoPopover');
  const opening = p.style.display === 'none';
  p.style.display = opening ? 'block' : 'none';
  if (opening) { refreshCacheStats(); seedLastSync(); }
}

async function refreshCacheStats() {
  try {
    const s = await api('/api/cache-stats');
    if (!s.available) return;
    const used = s.cacheBytes != null ? fmtB(s.cacheBytes) : '?';
    const free = s.diskFreeBytes != null ? ` · ${fmtB(s.diskFreeBytes)} free` : '';
    const scanned = s.scannedAt ? ` · scanned ${timeAgo(new Date(s.scannedAt))}` : '';
    document.getElementById('infoCacheSize').textContent = `${used}${free}${scanned}`;
    // Seed Last Scan from the backend so it survives page refreshes.
    if (!state.lastScanTime && s.scannedAt) { state.lastScanTime = new Date(s.scannedAt); updateInfoPopover(); }
  } catch { /* leave placeholder */ }
}

// Last Sync only lived in page memory — seed it from run history (newest first).
async function seedLastSync() {
  if (state.lastSyncTime) return;
  try {
    const runs = await api('/api/history');
    if (runs && runs.length) { state.lastSyncTime = new Date(runs[0].finishedAt || runs[0].startedAt); updateInfoPopover(); }
  } catch { /* leave placeholder */ }
}

export function updateInfoPopover() {
  if (state.lancacheIp) document.getElementById('infoLancacheIp').textContent = state.lancacheIp;
  if (state.lastSyncTime) document.getElementById('infoLastSync').textContent = timeAgo(state.lastSyncTime);
  if (state.lastScanTime) document.getElementById('infoLastScan').textContent = timeAgo(state.lastScanTime);
}

export function initInfoPopover() {
  document.addEventListener('click', e => {
    const p = document.getElementById('infoPopover');
    const btn = document.getElementById('infoBtn');
    if (p && p.style.display === 'block' && !p.contains(e.target) && !btn.contains(e.target))
      p.style.display = 'none';
  });
}