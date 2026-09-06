import { api } from '../api.js';
import { showToast } from '../ui/toast.js';
import { t } from '../i18n/i18n.js';

export async function loadSettings() {
  try {
    const s = await api('/api/settings');
    document.getElementById('setPrefillSchedule').value = s.prefill_schedule || '';
    document.getElementById('setScanSchedule').value = s.scan_schedule || '';
    document.getElementById('setScanConcurrency').value = s.scan_concurrency || '4';
    document.getElementById('setPrefillConcurrency').value = s.prefill_concurrency || '6';
    document.getElementById('setPrefillMaxMbps').value = s.prefill_max_mbps || '0';
    const oses = (s.prefill_os_filter || 'windows').split(',').map(o => o.trim().toLowerCase());
    document.getElementById('setOsWindows').checked = oses.includes('windows');
    document.getElementById('setOsLinux').checked = oses.includes('linux');
    document.getElementById('setOsMacos').checked = oses.includes('macos');
    document.getElementById('setLanguages').value = s.prefill_languages || '';
    const tz = document.getElementById('cronTzHint');
    if (tz && s.timezone) tz.textContent = `Cron schedules run in the container's timezone: ${s.timezone}`;
  } catch {}
}

export async function saveSettings() {
  const btn = document.querySelector('#tabSettings .btn'); btn.classList.add('btn-loading');
  try {
    const oses = [
      document.getElementById('setOsWindows').checked ? 'windows' : null,
      document.getElementById('setOsLinux').checked ? 'linux' : null,
      document.getElementById('setOsMacos').checked ? 'macos' : null,
    ].filter(Boolean);
    await api('/api/settings', { method: 'POST', body: JSON.stringify({
      prefill_schedule: document.getElementById('setPrefillSchedule').value,
      scan_schedule: document.getElementById('setScanSchedule').value,
      scan_concurrency: document.getElementById('setScanConcurrency').value,
      prefill_concurrency: document.getElementById('setPrefillConcurrency').value,
      prefill_max_mbps: document.getElementById('setPrefillMaxMbps').value,
      prefill_os_filter: oses.length ? oses.join(',') : 'windows', // never zero platforms
      prefill_languages: document.getElementById('setLanguages').value
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