import { describe, it, expect } from 'vitest';
import { state } from '../src/state.js';

describe('state', () => {
  it('has default values', () => {
    expect(state.apps).toEqual([]);
    expect(state.lastVersion).toBe(-1);
    expect(state.sseRetryDelay).toBe(1500);
    expect(state.cacheLoaded).toBe(false);
  });

  it('is mutable', () => {
    state.apps = [730, 440];
    expect(state.apps).toEqual([730, 440]);
    state.apps = [];
  });
});