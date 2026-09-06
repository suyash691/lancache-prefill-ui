import { state } from '../state.js';
import { api } from '../api.js';
import { updatePrefillUI, setBtns } from './prefill.js';
import { updateScanUI } from './scan.js';
import { updateInfoPopover } from '../ui/info.js';
import { loadApps } from '../pages/selected.js';

function scheduleRetry() {
  state.sseRetryDelay = Math.min(state.sseRetryDelay * 1.5, state.sseMaxRetry);
  setTimeout(startSSE, state.sseRetryDelay);
}

export async function startSSE() {
  if (window._sse) window._sse.close();
  // Exchange the session token (sent as a header) for a single-use, short-lived
  // ticket so the long-lived token never appears in the EventSource URL.
  let ticket;
  try {
    ({ ticket } = await api('/api/sse-ticket', { method: 'POST' }));
  } catch (e) {
    if (e.message !== 'unauthorized') scheduleRetry();
    return;
  }
  const sse = new EventSource(`/api/events?ticket=${encodeURIComponent(ticket)}`);
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
    scheduleRetry();
  };
}