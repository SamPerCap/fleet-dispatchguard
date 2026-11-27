import { Component, useEffect, useRef, useState, type ReactNode } from 'react';
import { language } from '../language';
import { currentAnchorOf, placeOf, type Place } from '../route';
import { useLanguage } from '../useLanguage';
import { AboutPage } from './AboutPage';
import type { createColdStart } from '../coldStart';
import { QUIET_MS } from '../quietTime';
import { prefersReducedMotion } from '../motion';
import { createAppLoader, reloadMarkOfTheTab } from './appLoader';
import { LoadingScene } from './LoadingScene';
import { closingMarkOfTheTab, createLoadingScene, type ScenePhase } from './loadingPhases';
import { Cover } from './Cover';
import { EngineeringPage } from './EngineeringPage';
import type { SeenPage } from './seenPages';
import { showcaseText } from './showcaseText';

function placeNow(): Place {
  const current = currentAnchorOf(location.hash);
  if (current) history.replaceState(history.state, '', location.pathname + location.search + current);
  return placeOf(location.hash, location.search);
}

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
  // Keyed by place: after an error, going Back or Forward to another place paints it again instead of keeping the error.
  return (
    <PageGuard key={place}>
      {place === 'cover' ? (
        <Cover />
      ) : place === 'engineering' ? (
        <EngineeringPage />
      ) : place === 'about' ? (
        <AboutPage />
      ) : (
        <Application seen={seen.current} language={chosen} />
      )}
    </PageGuard>
  );
}

/** Without it, one error while painting leaves the whole page white (React removes everything) and Back cannot bring it back. */
class PageGuard extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() {
    return { failed: true };
  }
  componentDidCatch(error: unknown) {
    console.error('The page could not be shown', error);
  }
  render() {
    if (!this.state.failed) return this.props.children;
    const text = showcaseText();
    return <Notice message={text.pageFailed} action={text.tryAgain} onAction={() => location.reload()} />;
  }
}

function Notice({ message, action, onAction }: { message: string; action: string; onAction: () => void }) {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 p-6" lang={language()}>
      <p role="alert" className="text-data text-ink-2">
        {message}
      </p>
      <button type="button" className="btn btn-primary" onClick={onAction}>
        {action}
      </button>
    </main>
  );
}

type LoadedApplication = Awaited<ReturnType<typeof loadApplication>>;
type ColdStart = ReturnType<typeof createColdStart>;

/**
 * One scene from "Try it" to the application (SPEC 0101): the code, the session and the database are awaited together behind the
 * same red light. The cold start and the session are imported on demand, so the cover still asks nothing (SPEC 0093 R1).
 */
function Application({ seen, language: chosen }: { seen: SeenPage[]; language: string }) {
  const [loaded, setLoaded] = useState<{ View: LoadedApplication } | null>(null);
  const [phase, setPhase] = useState<ScenePhase>('quiet');
  const [databaseWaking, setDatabaseWaking] = useState(false);
  const coldStart = useRef<ColdStart | null>(null);

  useEffect(() => {
    let live = true;
    const scene = createLoadingScene({ onPhase: setPhase, closing: closingMarkOfTheTab, reducedMotion: prefersReducedMotion(), quietMs: QUIET_MS });
    const fail = (failure: unknown) => {
      // Kept visible: the notice alone said nothing of the cause (SPEC 0100 R7).
      console.error('The application could not be loaded', failure);
      if (live) scene.applicationFailed();
    };
    loadApplication().then(View => {
      if (!live) return;
      setLoaded({ View });
      scene.application();
    }, fail);
    Promise.all([import('../coldStart'), import('../session')]).then(([{ createColdStart, isReady }, { session }]) => {
      if (!live) return;
      coldStart.current = createColdStart({
        ask: () => isReady(session.fetch),
        onState: state => {
          setDatabaseWaking(state === 'preparing');
          scene.database(state);
        },
      });
    }, fail);
    return () => {
      live = false;
      scene.stop();
      coldStart.current?.stop();
    };
  }, []);

  if (phase === 'in' && loaded) return <loaded.View seen={seen} language={chosen} />;
  if (phase === 'failed') {
    const text = showcaseText();
    return <Notice message={text.appFailed} action={text.tryAgain} onAction={() => location.reload()} />;
  }
  return <LoadingScene phase={phase} databaseWaking={databaseWaking} onRetry={() => coldStart.current?.retry()} />;
}
