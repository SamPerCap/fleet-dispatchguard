import { useCallback, useEffect, useState } from 'react';
import type { Loaded } from './home/data';

/** Discards responses of an earlier load. */
export function useLoad<T>(load: (() => Promise<T>) | null, deps: unknown[]): [Loaded<T> | null, () => void] {
  const [value, setValue] = useState<Loaded<T> | null>(null);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    if (!load) return;
    let live = true;
    setValue(null);
    load().then(
      v => live && setValue({ ok: true, value: v }),
      e => live && setValue({ ok: false, error: (e as Error).message }),
    );
    return () => {
      live = false;
    };
  }, [...deps, retry]); // eslint-disable-line react-hooks/exhaustive-deps
  return [value, useCallback(() => setRetry(n => n + 1), [])];
}
