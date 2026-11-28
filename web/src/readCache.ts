/**
 * Each request to the demo may start a fresh container and cost seconds (SPEC 0102), so what was just read is kept for a moment:
 * coming back to a view within that time paints at once. Memory only, for this tab.
 */
export const READ_KEPT_MS = 30_000;

interface Kept {
  value: unknown;
  at: number;
}

export function createReadCache(now: () => number = Date.now) {
  let kept = new Map<string, Kept>();
  let inFlight = new Map<string, Promise<unknown>>();

  return {
    get<T>(key: string, load: () => Promise<T>): Promise<T> {
      const found = kept.get(key);
      if (found && now() - found.at < READ_KEPT_MS) return Promise.resolve(found.value as T);
      const pending = inFlight.get(key);
      if (pending) return pending as Promise<T>;

      // Captured now: if the cache is cleared meanwhile, this answer belongs to the world before the write and is not kept.
      const keptNow = kept;
      const inFlightNow = inFlight;
      const loading = load().then(value => {
        if (keptNow === kept) keptNow.set(key, { value, at: now() });
        return value;
      });
      const forget = () => inFlightNow.delete(key);
      loading.then(forget, forget);
      inFlight.set(key, loading);
      return loading;
    },
    clear() {
      kept = new Map();
      inFlight = new Map();
    },
  };
}

const TIMESTAMP = /(\d{4}-\d{2}-\d{2}T\d{2}:\d{2})[:.\dZ+-]*/g;

/** The "now" of a view changes every millisecond; to the minute, two visits to the same view ask the same thing. */
export function readKeyOf(url: string): string {
  return decodeURIComponent(url).replace(TIMESTAMP, '$1');
}
