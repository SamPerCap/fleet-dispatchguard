import { useEffect, useRef, useState } from 'react';
import { language } from '../language';
import { placeOf, type Place } from '../route';
import { useLanguage } from '../useLanguage';
import { AboutPage } from './AboutPage';
import { createAppLoader, reloadMarkOfTheTab } from './appLoader';
import { Cover } from './Cover';
import { EngineeringPage } from './EngineeringPage';
import type { SeenPage } from './seenPages';
import { showcaseText } from './showcaseText';

const placeNow = (): Place => placeOf(location.hash, location.search);

// On demand: the code that starts the session is not even there before it is wanted (SPEC 0093 R1). Its texts come with it.
const loadApplication = createAppLoader(
  () =>
    Promise.all([import('../DispatcherApp'), import('../i18n/t').then(module => module.catalogueReady())]).then(
      ([module]) => module.DispatcherApp,
    ),
  () => location.reload(),
  reloadMarkOfTheTab,
);

/** Names of the visit timeline, read only by the author: in Spanish whatever the language (SPEC 0095 R7). */
const SEEN_NAME: Record<Exclude<Place, 'application'>, string> = { cover: 'Portada', engineering: 'Cómo está hecho', about: 'Sobre mí' };

export function Root() {
  const [place, setPlace] = useState(placeNow);
  const chosen = useLanguage();
  const seen = useRef<SeenPage[]>([]);

  useEffect(() => {
    const onAddress = () => setPlace(placeNow());
    addEventListener('hashchange', onAddress);
    addEventListener('popstate', onAddress);
    return () => {
      removeEventListener('hashchange', onAddress);
      removeEventListener('popstate', onAddress);
    };
  }, []);

  useEffect(() => {
    document.documentElement.lang = chosen;
    document.title = `Fleet DispatchGuard · ${showcaseText().tagline}`;
  }, [chosen]);

  useEffect(() => {
    if (place === 'application') return;
    seen.current.push({ name: SEEN_NAME[place], at: Date.now() });
  }, [place]);

  // The language is read by every text: whatever changes it paints everything again, without losing what is open or typed.
  if (place === 'cover') return <Cover />;
  if (place === 'engineering') return <EngineeringPage />;
  if (place === 'about') return <AboutPage />;
  return <Application seen={seen.current} language={chosen} />;
}

type LoadedApplication = Awaited<ReturnType<typeof loadApplication>>;

function Application({ seen, language: chosen }: { seen: SeenPage[]; language: string }) {
  const [loaded, setLoaded] = useState<{ View: LoadedApplication } | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let live = true;
    loadApplication().then(
      View => live && setLoaded({ View }),
      () => live && setFailed(true),
    );
    return () => {
      live = false;
    };
  }, []);

  if (loaded) return <loaded.View seen={seen} language={chosen} />;
  if (!failed) return null;
  const text = showcaseText();
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 p-6" lang={language()}>
      <p role="alert" className="text-data text-ink-2">
        {text.appFailed}
      </p>
      <button type="button" className="btn btn-primary" onClick={() => location.reload()}>
        {text.tryAgain}
      </button>
    </main>
  );
}
