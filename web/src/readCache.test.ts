import { describe, expect, it, vi } from 'vitest';
import { createReadCache, READ_FRESH_MS, READ_KEPT_MS, readKeyOf } from './readCache';

function clock(start = 0) {
  let now = start;
  return { now: () => now, advance: (ms: number) => (now += ms) };
}

const settled = () => new Promise(resolve => setTimeout(resolve, 0));

describe('reads kept for the tab (SPEC 0102)', () => {
  it('R1: a fresh read is answered without asking again', async () => {
    const time = clock();
    const cache = createReadCache(time.now);
    const load = vi.fn(async () => 'answer');

    await cache.get('a', load);
    time.advance(READ_FRESH_MS - 1);
    expect(await cache.get('a', load)).toBe('answer');

    expect(load).toHaveBeenCalledTimes(1);
  });

  it('R1: an older read is painted at once, asked again behind, and the change is announced', async () => {
    const time = clock();
    const refreshed = vi.fn();
    const cache = createReadCache(time.now, refreshed);
    const load = vi.fn().mockResolvedValueOnce('old').mockResolvedValueOnce('new');

    await cache.get('a', load);
    time.advance(READ_FRESH_MS);
    expect(await cache.get('a', load)).toBe('old');
    await settled();

    expect(load).toHaveBeenCalledTimes(2);
    expect(refreshed).toHaveBeenCalledTimes(1);
    expect(await cache.get('a', load)).toBe('new');
  });

  it('R1: past the time a read is kept, it is asked again before answering', async () => {
    const time = clock();
    const cache = createReadCache(time.now);
    const load = vi.fn().mockResolvedValueOnce('old').mockResolvedValueOnce('new');

    await cache.get('a', load);
    time.advance(READ_KEPT_MS);

    expect(await cache.get('a', load)).toBe('new');
  });

  it('R1: an identical read in flight is shared', async () => {
    const cache = createReadCache(clock().now);
    let finish: (value: string) => void = () => {};
    const load = vi.fn(() => new Promise<string>(resolve => (finish = resolve)));

    const first = cache.get('a', load);
    const second = cache.get('a', load);
    finish('answer');

    expect(await Promise.all([first, second])).toEqual(['answer', 'answer']);
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('R4: a failure is not kept, and a failed refresh behind keeps the old value quietly', async () => {
    const time = clock();
    const refreshed = vi.fn();
    const cache = createReadCache(time.now, refreshed);
    const load = vi.fn().mockRejectedValueOnce(new Error('down')).mockResolvedValueOnce('answer').mockRejectedValueOnce(new Error('down'));

    await expect(cache.get('a', load)).rejects.toThrow('down');
    expect(await cache.get('a', load)).toBe('answer');
    time.advance(READ_FRESH_MS);
    expect(await cache.get('a', load)).toBe('answer');
    await settled();

    expect(refreshed).not.toHaveBeenCalled();
  });

  it('R3: clear forgets everything', async () => {
    const cache = createReadCache(clock().now);
    const load = vi.fn(async () => 'answer');

    await cache.get('a', load);
    cache.clear();
    await cache.get('a', load);

    expect(load).toHaveBeenCalledTimes(2);
  });

  it('R3: a read that started before clear is not kept after it', async () => {
    const cache = createReadCache(clock().now);
    let finish: (value: string) => void = () => {};
    const slow = vi.fn(() => new Promise<string>(resolve => (finish = resolve)));
    const fresh = vi.fn(async () => 'fresh');

    const old = cache.get('a', slow);
    cache.clear();
    finish('old');
    await old;

    expect(await cache.get('a', fresh)).toBe('fresh');
  });
});

describe('the key of a read (SPEC 0102 R2)', () => {
  it('counts timestamps to the day and leaves the rest as it is', () => {
    expect(readKeyOf('/api/control/efficiency?date=2026-11-28T09:40:59.831Z&scope=2')).toBe('/api/control/efficiency?date=2026-11-28&scope=2');
    expect(readKeyOf('/api/control/activity?since=2026-11-27T09%3A40%3A12.004Z&kind=All')).toBe('/api/control/activity?since=2026-11-27&kind=All');
    expect(readKeyOf('/api/drivers')).toBe('/api/drivers');
  });
});
