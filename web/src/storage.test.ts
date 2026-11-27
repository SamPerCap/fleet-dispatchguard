import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { assignedBy, setAssignedBy } from './operator';
import { read, readScope, write, writeScope } from './storage';

/** The names before the rename lasted one release (SPEC 0094 R3, removed in 0099). */
describe('what the browser kept', () => {
  let stored: Map<string, string>;

  beforeEach(() => {
    stored = new Map();
    vi.stubGlobal('localStorage', { getItem: (key: string) => stored.get(key) ?? null, setItem: (key: string, value: string) => stored.set(key, value) });
  });

  afterEach(() => vi.unstubAllGlobals());

  it('a value is read back under its own key', () => {
    write('dispatchguard.baseId', '3');
    expect(readScope()).toBe(3);

    writeScope('all');
    expect(readScope()).toBe('all');
  });

  it('a value kept under a name before the rename is no longer read', () => {
    stored.set('lof.assignedBy', 'Ana');
    expect(assignedBy()).toBe('');
    expect(read('dispatchguard.assignedBy')).toBeNull();
  });

  it('a key that was never kept is not there', () => expect(read('dispatchguard.other')).toBeNull());

  it('without storage nothing is read and nothing fails', () => {
    vi.stubGlobal('localStorage', {
      getItem: () => {
        throw new Error('blocked');
      },
      setItem: () => {
        throw new Error('blocked');
      },
    });
    expect(read('dispatchguard.baseId')).toBeNull();
    expect(() => setAssignedBy('Ana')).not.toThrow();
  });
});
