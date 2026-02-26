import { state } from '../state.js';
import { t } from '../i18n/i18n.js';
import { loadLibrary } from '../pages/library.js';

export function switchTab(tab) {
  ['selected', 'library', 'evicted', 'cachebrowser', 'settings'].forEach((name, i) => {
    document.querySelectorAll('.tab')[i].classList.toggle('active', name === tab);
    document.getElementById('tab' + name[0].toUpperCase() + name.slice(1)).style.display = name === tab ? 'block' : 'none';
  });
  // Lazy-load tab content
  if (tab === 'library' && !state.library.length) {
    loadLibrary();
  }
  if (tab === 'evicted') {
    import('../pages/evicted.js').then(m => m.loadEvicted());
  }
  if (tab === 'cachebrowser') {
    import('../pages/cache-browser.js').then(m => m.loadCacheBrowser());
  }
  if (tab === 'settings') {
    import('../pages/settings.js').then(m => m.loadSettings());
  }
}

export function updateTabCounts() {
  const tabs = document.querySelectorAll('.tab');
  tabs[0].innerHTML = `${t('nav.selectedGames')}<span class="tab-count">(${state.apps.length})</span>`;
  tabs[1].innerHTML = `${t('nav.steamLibrary')}${state.library.length ? `<span class="tab-count">(${state.library.length})</span>` : ''}`;
  tabs[2].innerHTML = `${t('nav.evicted')}${state.evictedCount ? `<span class="tab-count">(${state.evictedCount})</span>` : ''}`;
}