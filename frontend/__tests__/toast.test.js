import { describe, it, expect, beforeEach } from 'vitest';
import { showToast } from '../src/ui/toast.js';

describe('showToast', () => {
  beforeEach(() => {
    document.body.innerHTML = '';
  });

  it('creates toast container if not present', () => {
    showToast('Test message', 'info');
    expect(document.getElementById('toastContainer')).not.toBeNull();
  });

  it('creates toast element with correct class', () => {
    showToast('Error!', 'error');
    const toast = document.querySelector('.toast-error');
    expect(toast).not.toBeNull();
    expect(toast.textContent).toContain('Error!');
  });

  it('creates success toast', () => {
    showToast('Done', 'success');
    expect(document.querySelector('.toast-success')).not.toBeNull();
  });

  it('stacks multiple toasts', () => {
    showToast('First', 'info');
    showToast('Second', 'success');
    const toasts = document.querySelectorAll('.toast');
    expect(toasts.length).toBe(2);
  });

  it('includes progress bar', () => {
    showToast('Test', 'info');
    expect(document.querySelector('.toast-bar')).not.toBeNull();
  });
});