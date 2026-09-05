import { api, esc, fmtB, timeAgo } from '../api.js';

export async function loadActivity() {
  let resp;
  try { resp = await api('/api/activity'); }
  catch { return; }

  document.getElementById('activityUnavailable').style.display = resp.available ? 'none' : 'block';
  document.getElementById('activityContent').style.display = resp.available ? 'block' : 'none';
  if (!resp.available || !resp.stats) return;

  const s = resp.stats;
  document.getElementById('actHitRatio').textContent =
    s.hitRatio != null ? `${(s.hitRatio * 100).toFixed(1)}%` : '—';
  document.getElementById('actHitBytes').textContent = fmtB(s.hitBytes);
  document.getElementById('actMissBytes').textContent = fmtB(s.missBytes);

  // Stacked CSS bar chart: green = hits, red = misses, normalized per bucket set.
  const chart = document.getElementById('activityChart');
  const max = Math.max(1, ...s.buckets.map(b => b.hitBytes + b.missBytes));
  chart.innerHTML = s.buckets.map(b => {
    const total = b.hitBytes + b.missBytes;
    const h = Math.max(2, Math.round((total / max) * 100));
    const hitPct = total > 0 ? (b.hitBytes / total) * 100 : 0;
    const label = `${new Date(b.start).toLocaleTimeString()} — hit ${fmtB(b.hitBytes)}, miss ${fmtB(b.missBytes)}`;
    return `<div class="activity-bar" style="height:${h}%" title="${esc(label)}">
      <div class="activity-bar-hit" style="height:${hitPct}%"></div></div>`;
  }).join('');

  const clients = document.getElementById('activityClients');
  clients.innerHTML = s.clients.length ? s.clients.map(c => {
    const total = c.hits + c.misses;
    const pct = total > 0 ? ((c.hits / total) * 100).toFixed(0) : '—';
    return `<div class="tr tr-clients"><span>${esc(c.ip)}</span><span>${fmtB(c.bytes)}</span>`
      + `<span>${pct}%</span><span>${timeAgo(new Date(c.lastSeen))}</span></div>`;
  }).join('') : '<div class="empty">No traffic observed yet.</div>';
}
