import { describe, it, expect, beforeEach } from 'vitest';
import { initTheme, toggleTheme } from '../src/ui/theme.js';

describe('theme', () => {
  beforeEach(() => {
    document.documentElement.setAttribute('data-theme', 'dark');
    document.body.innerHTML = '<button id="themeToggle">🌙</button>';
    localStorage.clear();
  });

  it('toggles from dark to light', () => {
    toggleTheme();
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    expect(localStorage.getItem('theme')).toBe('light');
    expect(document.getElementById('themeToggle').textContent).toBe('☀️');
  });

  it('toggles back to dark', () => {
    toggleTheme(); // → light
    toggleTheme(); // → dark
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(document.getElementById('themeToggle').textContent).toBe('🌙');
  });

  it('initTheme restores from localStorage', () => {
    localStorage.setItem('theme', 'light');
    initTheme();
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('initTheme keeps dark when no stored value', () => {
    initTheme();
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });
});