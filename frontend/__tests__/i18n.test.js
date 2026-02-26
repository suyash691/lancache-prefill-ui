import { describe, it, expect } from 'vitest';
import { t, randomTip, getLocale, getAvailableLocales } from '../src/i18n/i18n.js';

describe('i18n', () => {
  it('returns simple string', () => {
    expect(t('app.title')).toBe('Lancache Prefill');
  });

  it('returns nested key', () => {
    expect(t('toast.prefillStarted')).toBe('Prefill started');
  });

  it('replaces positional arguments', () => {
    expect(t('toast.requestFailed', 404)).toBe('Request failed (404)');
  });

  it('replaces multiple arguments', () => {
    expect(t('confirm.addFilteredMsg', 5, 'counter')).toBe('Add 5 games matching "counter"?');
  });

  it('returns key for missing translation', () => {
    expect(t('nonexistent.key')).toBe('nonexistent.key');
  });

  it('returns key for partial path', () => {
    expect(t('app.nonexistent')).toBe('app.nonexistent');
  });

  it('returns array for tips', () => {
    const tips = t('tips');
    expect(Array.isArray(tips)).toBe(true);
    expect(tips.length).toBe(10);
  });

  it('randomTip returns a string', () => {
    const tip = randomTip();
    expect(typeof tip).toBe('string');
    expect(tip.length).toBeGreaterThan(0);
  });

  it('getLocale returns en by default', () => {
    expect(getLocale()).toBe('en');
  });

  it('getAvailableLocales includes en', () => {
    expect(getAvailableLocales()).toContain('en');
  });
});