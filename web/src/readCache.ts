/**
 * Each request to the demo may start a fresh container and cost seconds (SPEC 0102), so what was read is kept for the tab: a
 * view coming back paints at once from it, and an older value is asked again behind, the views reloading when it arrives
 * (stale-while-revalidate). Memory only.
 */
export const READ_FRESH_MS = 30_000;
/** Older than this, a value is not worth painting: it is asked for before answering. */
export const READ_KEPT_MS = 10 * 60_000;

interface Kept {
  value: unknown;
  at: number;
}

export function createReadCache(now: () => number = Date.now, onRefreshed: () => void = () => {}) {
  let kept = new Map<string, Kept>();
  let inFlight = new Map<string, Promise<unknown>>();

  const load = <T>(key: string, ask: () => Promise<T>): Promise<T> => {
    const pending = inFlight.get(key);
    if (pending) return pending as Promise<T>;
    // Captured now: if the cache is cleared meanwhile, this answer belongs to the world before the write and is not kept.
    const keptNow = kept;
    const inFlightNow = inFlight;
    const loading = ask().then(value => {
      if (keptNow === kept) keptNow.set(key, { value, at: now() });
      return value;
    });
    const forget = () => inFlightNow.delete(key);
    loading.then(forget, forget);
    inFlight.set(key, loading);
    return loading;
  };

  return {
    get<T>(key: string, ask: () => Promise<T>): Promise<T> {
      const found = kept.get(key);
      const age = found ? now() - found.at : Infinity;
      if (age < READ_FRESH_MS) return Promise.resolve(found!.value as T);
      if (age < READ_KEPT_MS) {
        const refreshing = !inFlight.has(key);
        const behind = load(key, ask);
        // A failure behind keeps the old value: the next refresh of the view will ask again.
        if (refreshing) behind.then(onRefreshed, () => {});
        return Promise.resolve(found!.value as T);
      }
      return load(key, ask);
    },
    clear() {
      kept = new Map();
      inFlight = new Map();
    },
  };
}

const TIMESTAMP = /(\d{4}-\d{2}-\d{2})T[\d:.]+Z?/g;

/**
 * The "now" of a view changes every millisecond: to the day, coming back to the same view finds what it showed. A value painted
 * from an earlier hour is replaced as soon as the refresh behind arrives.
 */
export function readKeyOf(url: string): string {
  return decodeURIComponent(url).replace(TIMESTAMP, '$1');
}
