import { state } from '../state.js';
import { timeAgo } from '../api.js';
import { t } from '../i18n/i18n.js';

export function toggleInfoPopover() {
  const p = document.getElementById('infoPopover');
  p.style.display = p.style.display === 'none' ? 'block' : 'none';
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