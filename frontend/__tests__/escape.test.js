import { describe, it, expect } from 'vitest';
import { esc } from '../src/api.js';

describe('esc', () => {
  it('escapes angle brackets (prevents tag injection)', () => {
    expect(esc('<img src=x onerror=alert(1)>')).toBe('&lt;img src=x onerror=alert(1)&gt;');
  });

  it('escapes ampersand', () => {
    expect(esc('Tom & Jerry')).toBe('Tom &amp; Jerry');
  });

  it('escapes double and single quotes', () => {
    expect(esc('a"b\'c')).toBe('a&quot;b&#39;c');
  });

  it('escapes a script tag payload', () => {
    expect(esc('<script>alert(1)</script>')).toBe('&lt;script&gt;alert(1)&lt;/script&gt;');
  });

  it('returns empty string for null/undefined', () => {
    expect(esc(null)).toBe('');
    expect(esc(undefined)).toBe('');
  });

  it('stringifies non-strings', () => {
    expect(esc(730)).toBe('730');
  });

  it('leaves benign text unchanged', () => {
    expect(esc('Counter-Strike 2')).toBe('Counter-Strike 2');
  });
});
