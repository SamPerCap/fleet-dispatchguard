import { useEffect, useState } from 'react';
import { api, control } from './api';
import { App } from './App';
import { controlReads } from './home/requests';
import { isPage } from './pageIds';
import { readScope } from './storage';
import type { SeenPage } from './showcase/seenPages';
import { visit } from './visit';

/** Everything that asks the server: loaded only when the visitor goes in (SPEC 0093 R1). */
export function DispatcherApp({ seen, language }: { seen: SeenPage[]; language: string }) {
  const [comesFromTheCover] = useState(() => seen.length > 0);
  useEffect(() => {
    for (const page of seen.splice(0)) visit.screen(page.name, page.at);
  }, [seen]);
  return (
    // The database already answered: the loading scene of the showcase waits for it (SPEC 0101).
    <App offerTour={comesFromTheCover} language={language} />
  );
}

/**
 * Started behind the loading scene once the code and the database are ready (SPEC 0102 R6): the first view then finds its answers
 * kept. What fails here is simply asked again by the view.
 */
export function prefetchFirstView() {
  void Promise.allSettled([api.drivers(), api.vehicles(), api.locations()]);
  const hashPage = location.hash.slice(1);
  if (isPage(hashPage) && hashPage !== 'control') return;
  void Promise.allSettled([control.bases(), api.rules()]);
  void controlReads(readScope(), new Date());
}
