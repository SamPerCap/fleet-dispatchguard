import { describe, expect, it, vi } from 'vitest';
import { createReadCache, READ_KEPT_MS, readKeyOf } from './readCache';

function clock(start = 0) {
  let now = start;
  return { now: () => now, advance: (ms: number) => (now += ms) };
}

describe('reads kept for the tab (SPEC 0102)', () => {
  it('R1: the same read within 30 s is answered without asking again', async () => {
    const time = clock();
    const cache = createReadCache(time.now);
    const load = vi.fn(async () => 'answer');

    await cache.get('a', load);
    time.advance(READ_KEPT_MS - 1);
    expect(await cache.get('a', load)).toBe('answer');

    expect(load).toHaveBeenCalledTimes(1);
  });

  it('R1: after 30 s it asks again', async () => {
    const time = clock();
    const cache = createReadCache(time.now);
    const load = vi.fn(async () => 'answer');

    await cache.get('a', load);
    time.advance(READ_KEPT_MS);
    await cache.get('a', load);

    expect(load).toHaveBeenCalledTimes(2);
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

  it('R4: a failure is not kept', async () => {
    const cache = createReadCache(clock().now);
    const load = vi.fn().mockRejectedValueOnce(new Error('down')).mockResolvedValueOnce('answer');

    await expect(cache.get('a', load)).rejects.toThrow('down');
    expect(await cache.get('a', load)).toBe('answer');
    expect(load).toHaveBeenCalledTimes(2);
  });

  it('R3: clear forgets everything, including a read in flight', async () => {
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
  it('counts timestamps to the minute and leaves the rest as it is', () => {
    expect(readKeyOf('/api/control/efficiency?date=2026-11-28T09:40:59.831Z&scope=2')).toBe(
      '/api/control/efficiency?date=2026-11-28T09:40&scope=2',
    );
    expect(readKeyOf('/api/control/activity?since=2026-11-27T09%3A40%3A12.004Z&kind=All')).toBe(
      '/api/control/activity?since=2026-11-27T09:40&kind=All',
    );
    expect(readKeyOf('/api/drivers')).toBe('/api/drivers');
  });
});
