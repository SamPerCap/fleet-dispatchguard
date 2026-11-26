import { useCallback, useEffect, useRef, useState } from 'react';
import { readResource, sameResource, writeResource, type ResourceKind, type ResourceRef, type ResourceTab } from './resourceUrl';

/**
 * The resource card lives in the URL. Opening it adds a history entry (so "back" closes it); switching tab or resource while open
 * replaces it. Closing it with X/Esc/outside goes back if the entry is ours; if it came from a link, or it closes to go somewhere
 * else (booking, view, catalogue), it is replaced (an asynchronous `back` would change the `#`).
 */
export function useResourceUrl() {
  const [resource, setResource] = useState<ResourceRef | null>(() => readResource(location.search));
  const resourceRef = useRef(resource);
  resourceRef.current = resource;
  const setUrl = useCallback((ref: ResourceRef | null, mode: 'push' | 'replace') => {
    const url = `${location.pathname}${writeResource(location.search, ref)}${location.hash}`;
    const state = { ...(history.state ?? {}), openedResource: ref != null && (mode === 'push' || !!history.state?.openedResource) };
    if (mode === 'push') history.pushState(state, '', url);
    else history.replaceState(state, '', url);
  }, []);
  // `history.back()` is asynchronous: if another card opens before its `popstate` arrives, writing the URL waits for it (otherwise
  // the pending `back` would undo the new entry and reopen the previous card).
  const pendingBack = useRef(false);
  const queued = useRef<ResourceRef | null>(null);
  const showResource = useCallback(
    (kind: ResourceKind, id: number, tab: ResourceTab = 'resumen') => {
      const ref: ResourceRef = { kind, id, tab };
      if (pendingBack.current) queued.current = ref;
      else setUrl(ref, resourceRef.current ? 'replace' : 'push');
      setResource(ref);
    },
    [setUrl],
  );
  const setResourceTab = useCallback(
    (tab: ResourceTab) => {
      const current = resourceRef.current;
      if (!current || current.tab === tab) return;
      const ref = { ...current, tab };
      if (pendingBack.current) queued.current = ref;
      else setUrl(ref, 'replace');
      setResource(ref);
    },
    [setUrl],
  );
  /** `away`: closed in order to go somewhere else. */
  const closeResource = useCallback(
    (away = false) => {
      if (!resourceRef.current) return;
      setResource(null);
      if (pendingBack.current) {
        queued.current = null;
        return;
      }
      if (!away && history.state?.openedResource) {
        pendingBack.current = true;
        history.back();
      } else setUrl(null, 'replace');
    },
    [setUrl],
  );
  useEffect(() => {
    const onPop = () => {
      if (pendingBack.current) {
        pendingBack.current = false;
        const next = queued.current;
        queued.current = null;
        if (next) {
          setUrl(next, 'push');
          setResource(next);
        }
        return;
      }
      setResource(current => {
        const next = readResource(location.search);
        return sameResource(current, next) && current?.tab === next?.tab ? current : next;
      });
    };
    addEventListener('popstate', onPop);
    return () => removeEventListener('popstate', onPop);
  }, [setUrl]);

  return { resource, resourceRef, showResource, setResourceTab, closeResource };
}
