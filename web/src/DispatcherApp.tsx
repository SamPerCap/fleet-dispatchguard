import { useEffect, useState } from 'react';
import { App } from './App';
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
