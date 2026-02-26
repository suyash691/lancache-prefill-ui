import { state } from '../state.js';
import { getToken } from '../api.js';
import { updatePrefillUI, setBtns } from './prefill.js';
import { updateScanUI } from './scan.js';
import { updateInfoPopover } from '../ui/info.js';
import { loadApps } from '../pages/selected.js';

export function startSSE() {
  if (window._sse) window._sse.close();
  const sse = new EventSource(`/api/events?token=${encodeURIComponent(getToken())}`);
  window._sse = sse;
  state.sseRetryDelay = 1500;
  sse.onmessage = e => {
    try {
      const d = JSON.parse(e.data);
      state.syncQueue = d.syncQueue || [];
      state.isQueuedPrefill = d.syncQueue && d.syncQueue.length > 0 || d.prefill?.total === 1;
      if (d.version !== undefined && d.version !== state.lastVersion && state.lastVersion !== -1) loadApps();
      state.lastVersion = d.version ?? state.lastVersion;
      updatePrefillUI(d.prefill);
      updateScanUI(d.scan);
      setBtns(d.activeJob);
      updateInfoPopover();
    } catch {}
  };
  sse.onerror = () => {
    sse.close();
    state.sseRetryDelay = Math.min(state.sseRetryDelay * 1.5, state.sseMaxRetry);
    setTimeout(startSSE, state.sseRetryDelay);
  };
}