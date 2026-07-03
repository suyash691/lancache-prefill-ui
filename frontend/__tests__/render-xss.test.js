import { describe, it, expect, beforeEach } from 'vitest';
import { state } from '../src/state.js';
import { renderApps } from '../src/pages/selected.js';

// Regression test for DOM XSS: a malicious Steam-supplied game name must be
// rendered as inert text, not as live HTML, in the Selected Games list.
describe('renderApps XSS safety', () => {
  const PAYLOAD = '<img src=x onerror=alert(1)>';

  beforeEach(() => {
    document.body.innerHTML = '<div id="rows"></div><select id="sortSelected"><option value="name">name</option></select>';
    state.apps = [730];
    state.appNames = { 730: PAYLOAD };
    state.scanMap = {};
    state.utd = {};
    state.manifests = {};
    state.cachedManifests = {};
    state.appStatus = {};
  });

  it('escapes a malicious game name instead of injecting a tag', () => {
    renderApps();
    const html = document.getElementById('rows').innerHTML;
    // The payload appears only in escaped form...
    expect(html).toContain('&lt;img src=x onerror=alert(1)&gt;');
    // ...and never as a raw injected element.
    expect(html).not.toContain('<img src=x onerror=alert(1)>');
  });

  it('does not create a rogue element from the name', () => {
    renderApps();
    // The name is inside the game-name span as text; no child element came from it.
    const nameSpan = document.querySelector('.game-name > span');
    expect(nameSpan).not.toBeNull();
    expect(nameSpan.textContent).toBe(PAYLOAD);
    expect(nameSpan.querySelector('img')).toBeNull();
  });
});
