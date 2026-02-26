import { api } from '../api.js';
import { showToast } from '../ui/toast.js';
import { t } from '../i18n/i18n.js';

export async function loadSettings() {
  try {
    const s = await api('/api/settings');
    document.getElementById('setPrefillSchedule').value = s.prefill_schedule || '';
    document.getElementById('setScanSchedule').value = s.scan_schedule || '';
    document.getElementById('setScanConcurrency').value = s.scan_concurrency || '4';
  } catch {}
}

export async function saveSettings() {
  const btn = document.querySelector('#tabSettings .btn'); btn.classList.add('btn-loading');
  try {
    await api('/api/settings', { method: 'POST', body: JSON.stringify({
      prefill_schedule: document.getElementById('setPrefillSchedule').value,
      scan_schedule: document.getElementById('setScanSchedule').value,
      scan_concurrency: document.getElementById('setScanConcurrency').value
    }) });
    const saved = document.getElementById('settingsSaved');
    saved.style.display = 'inline'; setTimeout(() => saved.style.display = 'none', 3000);
    showToast(t('settings.settingsSaved'), 'success');
  } catch { showToast(t('settings.failedSave'), 'error'); }
  finally { btn.classList.remove('btn-loading'); }
}

export async function reconcileCache() {
  const btn = document.getElementById('bReconcile'); btn.classList.add('btn-loading');
  try { await api('/api/scan/reconcile', { method: 'POST' }); showToast(t('toast.reconcileStarted'), 'success'); }
  catch (e) { if (e.message !== 'conflict') showToast(t('toast.failed'), 'error'); }
  finally { btn.classList.remove('btn-loading'); }
}