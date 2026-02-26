import { describe, it, expect } from 'vitest';
import { fmtB, timeAgo, progressBar } from '../src/api.js';

describe('fmtB', () => {
  it('formats bytes as KB', () => { expect(fmtB(500000)).toBe('500 KB'); });
  it('formats bytes as MB', () => { expect(fmtB(50000000)).toBe('50.0 MB'); });
  it('formats bytes as GB', () => { expect(fmtB(5000000000)).toBe('5.0 GB'); });
});

describe('timeAgo', () => {
  it('returns "just now" for recent', () => { expect(timeAgo(new Date())).toBe('just now'); });
  it('returns minutes ago', () => { expect(timeAgo(new Date(Date.now() - 120000))).toBe('2m ago'); });
  it('returns hours ago', () => { expect(timeAgo(new Date(Date.now() - 7200000))).toBe('2h ago'); });
  it('returns days ago', () => { expect(timeAgo(new Date(Date.now() - 172800000))).toBe('2d ago'); });
});

describe('progressBar', () => {
  it('returns HTML with percentage', () => {
    const html = progressBar(50, 'prefill');
    expect(html).toContain('width:50%');
    expect(html).toContain('prefill-bar');
  });
  it('uses scan type', () => {
    expect(progressBar(75, 'scan')).toContain('scan-bar');
  });
});