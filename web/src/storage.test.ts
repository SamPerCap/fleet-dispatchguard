import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { assignedBy, setAssignedBy } from './operator';
import { read, readScope, write, writeScope } from './storage';

/** SPEC 0094 R3: what a browser kept before the rename is still there after it. */
describe('what the browser kept', () => {
  let stored: Map<string, string>;

  beforeEach(() => {
    stored = new Map();
    vi.stubGlobal('localStorage', { getItem: (key: string) => stored.get(key) ?? null, setItem: (key: string, value: string) => stored.set(key, value) });
  });

  afterEach(() => vi.unstubAllGlobals());

  it.each([
    ['dispatchguard.assignedBy', 'lof.assignedBy'],
    ['dispatchguard.baseId', 'lof.baseId'],
    ['dispatchguard.stripHours', 'lof.stripHours'],
    ['dispatchguard.stripMode', 'lof.stripMode'],
    ['dispatchguard.welcomeDismissed', 'betterbus.welcomeDismissed'],
  ])('«%s» is read from «%s» until it is written', (key, previousKey) => {
    stored.set(previousKey, 'before');
    expect(read(key)).toBe('before');

    write(key, 'after');
    expect(read(key)).toBe('after');
    expect(stored.get(previousKey)).toBe('before');
  });

  it('an empty new value wins', () => {
    stored.set('lof.assignedBy', 'Ana');
    expect(assignedBy()).toBe('Ana');

    setAssignedBy('');
    expect(assignedBy()).toBe('');
  });

  it('the base that was chosen before is still chosen', () => {
    stored.set('lof.baseId', '3');
    expect(readScope()).toBe(3);

    writeScope('all');
    expect(readScope()).toBe('all');
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
