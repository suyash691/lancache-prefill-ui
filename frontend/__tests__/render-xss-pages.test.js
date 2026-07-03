import { describe, it, expect, beforeEach } from 'vitest';
import { state } from '../src/state.js';
import { renderLibrary } from '../src/pages/library.js';
import { renderEvicted } from '../src/pages/evicted.js';

const PAYLOAD = '<img src=x onerror=alert(1)>';

describe('renderLibrary XSS safety', () => {
  beforeEach(() => {
    document.body.innerHTML =
      '<input id="libSearch" value="">' +
      '<div id="libRows"></div>' +
      '<div id="libLoadMore"></div>' +
      '<span id="libRemaining"></span>';
    state.library = [{ appId: 730, name: PAYLOAD, selected: false }];
    state.libPage = 1;
    state.libPageSize = 100;
  });

  it('escapes a malicious library game name', () => {
    renderLibrary();
    const html = document.getElementById('libRows').innerHTML;
    expect(html).toContain('&lt;img src=x onerror=alert(1)&gt;');
    expect(html).not.toContain('<img src=x onerror=alert(1)>');
  });

  it('renders the name as inert text (no injected element)', () => {
    renderLibrary();
    const nameSpan = document.querySelector('.game-name > span');
    expect(nameSpan.textContent).toContain(PAYLOAD);
    expect(nameSpan.querySelector('img')).toBeNull();
  });
});

describe('renderEvicted XSS safety', () => {
  beforeEach(() => {
    document.body.innerHTML = '<div id="evictedRows"></div>';
  });

  it('escapes a malicious evicted game name', () => {
    renderEvicted([{ appId: 730, name: PAYLOAD }]);
    const html = document.getElementById('evictedRows').innerHTML;
    expect(html).toContain('&lt;img src=x onerror=alert(1)&gt;');
    expect(html).not.toContain('<img src=x onerror=alert(1)>');
  });

  it('renders the name as inert text (no injected element)', () => {
    renderEvicted([{ appId: 730, name: PAYLOAD }]);
    const nameSpan = document.querySelector('.game-name > span');
    expect(nameSpan.textContent).toBe(PAYLOAD);
    expect(nameSpan.querySelector('img')).toBeNull();
  });
});
