import { api, esc } from '../api.js';
import { showToast } from '../ui/toast.js';
import { loadApps } from './selected.js';

let topGames = [];

export async function openTopGames() {
  const dlg = document.getElementById('quickAddDialog');
  const rows = document.getElementById('quickAddRows');
  document.getElementById('quickAddTitle').textContent = '★ Top Games';
  document.getElementById('quickAddDesc').textContent =
    'Most played on Steam right now. Unowned games can be selected, but prefill will fail on them (Steam only serves owned content).';
  rows.innerHTML = '<div class="empty"><div class="spinner"></div></div>';
  dlg.style.display = 'flex';
  try { topGames = await api('/api/library/top?n=50'); }
  catch { rows.innerHTML = '<div class="empty">Failed to load Steam charts.</div>'; return; }
  renderTopRows();
}

function renderTopRows() {
  const rows = document.getElementById('quickAddRows');
  rows.innerHTML = topGames.map(g =>
    `<div class="history-app" style="display:flex;gap:8px;align-items:center;padding:4px 0">
      <span style="width:24px;color:var(--muted)">${g.rank}</span>
      <span style="flex:1">${esc(g.name)}${g.owned ? '' : ' <span class="size-hint">(not owned)</span>'}</span>
      ${g.selected
        ? '<span class="badge g">added</span>'
        : `<button class="btn btn-g btn-s" data-add="${g.appId}">+ Add</button>`}
    </div>`).join('');
  rows.querySelectorAll('[data-add]').forEach(btn => btn.addEventListener('click', async () => {
    await addOne(parseInt(btn.dataset.add));
    const g = topGames.find(x => x.appId === parseInt(btn.dataset.add));
    if (g) g.selected = true;
    renderTopRows();
  }));
}

async function addOne(appId) {
  try { await api('/api/apps/add', { method: 'POST', body: JSON.stringify({ appId }) }); }
  catch { showToast('Failed to add app', 'error'); }
}

export async function quickAddAllOwned() {
  const targets = topGames.filter(g => g.owned && !g.selected);
  if (!targets.length) { showToast('Nothing to add — owned top games are already selected', 'info'); return; }
  for (const g of targets) { await addOne(g.appId); g.selected = true; }
  renderTopRows();
  showToast(`Added ${targets.length} game${targets.length > 1 ? 's' : ''}`, 'success');
  loadApps();
}

export async function addRecentPurchases() {
  let items;
  try { items = await api('/api/library/recent-purchases?days=14'); }
  catch { showToast('Failed to load recent licenses', 'error'); return; }
  const fresh = items.filter(i => !i.selected);
  if (!fresh.length) { showToast('No new purchases in the last 14 days', 'info'); return; }
  for (const i of fresh) await addOne(i.appId);
  showToast(`Added ${fresh.length} recent license${fresh.length > 1 ? 's' : ''}`, 'success');
  loadApps();
}
