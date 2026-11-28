import { t } from './i18n/t';
import { BusFront, CalendarRange, ChevronRight, ClipboardList, LayoutDashboard, Menu, Scale, Search, ShieldCheck, Users } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api, onReadsRefreshed } from './api';
import type { AssignTarget } from './assignPeriod';
import { AssignPeriodPanel } from './AssignPeriodPanel';
import { BookingPanel, type PanelTarget } from './BookingPanel';
import { BRAND } from './brand';
import { CommandPalette, isMac } from './CommandPalette';
import { BrandMark } from './brand/BrandArt';
import type { AlertTarget } from './control';
import { assignedBy, setAssignedBy } from './operator';
import type { PaletteItem } from './palette';
import { AuditPage } from './pages/AuditPage';
import { ControlPage } from './pages/ControlPage';
import type { BarExplanation } from './Timeline';
import { BookingsPage } from './pages/BookingsPage';
import { PlanningPage } from './pages/PlanningPage';
import { EnginePage } from './pages/EnginePage';
import { isPage, type Page } from './pageIds';
import { RulesPage } from './pages/RulesPage';
import { DriversPage, VehiclesPage } from './pages/ResourcesPages';
import { PrivacyNotice } from './PrivacyNotice';
import { ResourceModal } from './ResourceModal';
import type { ResourceKind } from './resourceUrl';
import { Avatar, ErrorBox } from './ui';
import { BACKDROP, NAV_ID, navPlacement } from './menu';
import { ABOUT_HASH, BREAK_THE_LAW_HASH, ENGINEERING_HASH, START_HASH, tourStartOf } from './route';
import { Tour } from './Tour';
import { useResourceUrl } from './useResourceUrl';
import type { Driver, Location, Trip, Vehicle } from './types';
import { listenToVisit, visit } from './visit';
import { screenName } from './visitLabels';
import { LanguageSwitch } from './LanguageSwitch';

const NAV: { id: Page; label: string; icon: JSX.Element; group?: string; keywords?: string }[] = [
  {
    id: 'control',
    get label() { return t.app.control; },
    icon: <LayoutDashboard className="h-[18px] w-[18px]" aria-hidden />,
    get group() { return t.app.operation; },
    get keywords() { return t.app.controlWords; },
  },
  {
    id: 'planning',
    get label() { return t.app.planning; },
    icon: <CalendarRange className="h-[18px] w-[18px]" aria-hidden />,
    get keywords() { return t.app.planningWords; },
  },
  { id: 'bookings', get label() { return t.app.bookings; }, icon: <ClipboardList className="h-[18px] w-[18px]" aria-hidden /> },
  { id: 'drivers', get label() { return t.app.drivers; }, get group() { return t.app.resources; }, icon: <Users className="h-[18px] w-[18px]" aria-hidden /> },
  { id: 'vehicles', get label() { return t.app.vehicles; }, icon: <BusFront className="h-[18px] w-[18px]" aria-hidden /> },
  {
    id: 'engine',
    get label() { return t.app.engine; },
    get group() { return t.app.compliance; },
    icon: <Scale className="h-[18px] w-[18px]" aria-hidden />,
    get keywords() { return t.app.engineWords; },
  },
];
/**
 * Not in the sidebar: the rules catalogue is reached from the rules engine page, which stays marked meanwhile, and the audit only by its
 * address, because it is for the author.
 */
const LABEL_OUT_OF_NAV: Partial<Record<Page, string>> = { get rules() { return t.app.rules; }, get audit() { return t.app.access; } };
const navOf = (page: Page): Page => (page === 'rules' ? 'engine' : page);
// Read when asked, never when the module loads: the texts arrive in parallel with this chunk, and the language can change later.
function groupOf(page: Page): string | undefined {
  let group: string | undefined;
  for (const item of NAV) {
    group = item.group ?? group;
    if (item.id === page) return group;
  }
  return undefined;
}
const labelOf = (page: Page) => LABEL_OUT_OF_NAV[page] ?? NAV.find(n => n.id === page)!.label;
/** `keywords` let the palette find a view by words that are not in its label. */
const VIEW_ITEMS: PaletteItem[] = [
  ...NAV.map(item => ({
    kind: 'view' as const,
    target: item.id,
    get label() { return item.label; },
    get detail() { return groupOf(item.id); },
    get keywords() { return item.keywords ?? ''; },
  })),
  { kind: 'view', target: 'rules', get label() { return t.app.rules; }, get detail() { return t.app.compliance; }, get keywords() { return t.app.rulesWords; } },
];

export interface MasterData {
  drivers: Driver[];
  vehicles: Vehicle[];
  locations: Location[];
  locName: (id: number) => string;
}

/** Names, the same in every language. */
const REGULATIONS = 'CE 561/2006 · UE 2020/1054';

export function App({ offerTour, language }: { offerTour: boolean; language: string }) {
  const [page, setPage] = useState<Page>(() => {
    const hashPage = location.hash.slice(1);
    return isPage(hashPage) ? hashPage : 'control';
  });
  const [tour, setTour] = useState(() => tourStartOf(location.hash, offerTour));
  const [rulesGroup, setRulesGroup] = useState<string | null>(null);
  const [operator, setOperator] = useState(assignedBy);
  const [drivers, setDrivers] = useState<Driver[]>([]);
  const [vehicles, setVehicles] = useState<Vehicle[]>([]);
  const [locations, setLocations] = useState<Location[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [panel, setPanel] = useState<PanelTarget | null>(null);
  const { resource, resourceRef, showResource, setResourceTab, closeResource } = useResourceUrl();
  // No stacked modals: the card replaces the booking panel.
  const openResource = useCallback(
    (kind: ResourceKind, id: number) => {
      showResource(kind, id);
      setPanel(null);
    },
    [showResource],
  );
  const [version, setVersion] = useState(0); // Bumped after every change so that the views reload.
  const [wantOptimizer, setWantOptimizer] = useState(false);
  // Kept for this session only: explanations are not persisted (SPEC 0042).
  const [explained, setExplained] = useState<Map<number, BarExplanation>>(() => new Map());
  const onAllocated = useCallback((id: number, info: BarExplanation) => setExplained(m => new Map(m).set(id, info)), []);
  const [palette, setPalette] = useState(false);
  const [privacy, setPrivacy] = useState(false);
  // Lives here, not in Planning, so that switching view halfway does not close it. It steps aside while a booking or a resource card
  // is open (SPEC 0068).
  const [assigning, setAssigning] = useState<AssignTarget | null>(null);
  const openAssign = useCallback((target: AssignTarget) => setAssigning(current => current ?? target), []);
  const covered = panel != null || resource != null;
  const assignShown = useRef(false);
  assignShown.current = assigning != null && !covered;

  // With the card open, Ctrl+K / ⌘K closes it before opening the palette (no stacked modals).
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        if (assignShown.current) return;
        if (resourceRef.current) closeResource(true);
        setPalette(o => !o);
      }
    };
    addEventListener('keydown', onKey);
    return () => removeEventListener('keydown', onKey);
  }, [closeResource, resourceRef]);

  const loadMaster = useCallback(async () => {
    try {
      const [loadedDrivers, loadedVehicles, loadedLocations] = await Promise.all([api.drivers(), api.vehicles(), api.locations()]);
      setDrivers(loadedDrivers);
      setVehicles(loadedVehicles);
      setLocations(loadedLocations);
      setError(null);
    } catch (cause) {
      setError((cause as Error).message);
    }
  }, []);
  useEffect(() => {
    void loadMaster();
  }, [loadMaster]);
  // Keeps the query string (resource card) and the entry state: only the `#` changes.
  useEffect(() => {
    history.replaceState(history.state, '', `#${page}`);
  }, [page]);
  useEffect(() => {
    visit.screen(screenName(page));
  }, [page]);
  useEffect(listenToVisit, []);
  // Every view starts at the top. The views that focus something (catalogue group, card) do it later, once their data is loaded.
  useEffect(() => {
    if (scrollY > 0) scrollTo({ top: 0 });
  }, [page]);
  useEffect(() => {
    const onHash = () => {
      const hashPage = location.hash.slice(1);
      if (isPage(hashPage)) setPage(hashPage);
    };
    addEventListener('hashchange', onHash);
    return () => removeEventListener('hashchange', onHash);
  }, []);
  // Without a step in the history: Back returns to the cover, and a reload does not search again.
  useEffect(() => {
    if (location.hash === BREAK_THE_LAW_HASH) history.replaceState(null, '', `${location.search}${START_HASH}`);
  }, []);

  const master: MasterData = useMemo(() => {
    const names = new Map(locations.map(l => [l.Id, l.Name]));
    return { drivers, vehicles, locations, locName: id => names.get(id) ?? `#${id}` };
  }, [drivers, vehicles, locations]);
  const onAction = useCallback(
    (target: AlertTarget, trips: Trip[]) => {
      if (target.kind === 'booking') setPanel({ bookingId: target.bookingId });
      else if (target.kind === 'trip') {
        const trip = trips.find(x => x.Id === target.tripId);
        if (trip) setPanel({ bookingId: trip.BookingId, tripId: trip.Id });
        else
          void api
            .trip(target.tripId)
            .then(x => setPanel({ bookingId: x.BookingId, tripId: x.Id }))
            .catch(e => setError((e as Error).message));
      } else openResource(target.kind, target.id);
    },
    [openResource],
  );
  // A view painted from kept reads reloads once the newer answers arrive behind it.
  useEffect(() => {
    const stopListening = onReadsRefreshed(() => setVersion(v => v + 1));
    return stopListening;
  }, []);
  const changed = useCallback(() => {
    setVersion(v => v + 1);
    void loadMaster();
  }, [loadMaster]);
  const [menuOpen, setMenuOpen] = useState(false);
  const go = useCallback((nextPage: Page) => {
    setRulesGroup(null);
    setPage(nextPage);
    setMenuOpen(false);
  }, []);
  useEffect(() => {
    if (!menuOpen) return;
    document.querySelector<HTMLElement>(`#${NAV_ID} button`)?.focus();
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && setMenuOpen(false);
    addEventListener('keydown', onKey);
    return () => {
      removeEventListener('keydown', onKey);
      document.querySelector<HTMLElement>(`[aria-controls="${NAV_ID}"]`)?.focus();
    };
  }, [menuOpen]);
  const pick = useCallback(
    (item: PaletteItem) => {
      if (item.kind === 'view') go(item.target as Page);
      else if (item.kind === 'booking') setPanel({ bookingId: item.target as number });
      else openResource(item.kind, item.target as number);
    },
    [go, openResource],
  );
  const openDriver = useCallback((id: number) => openResource('driver', id), [openResource]);
  const openVehicle = useCallback((id: number) => openResource('vehicle', id), [openResource]);
  // What the API words comes again in the new language; what is open or typed stays (SPEC 0095 R6).
  const shownLanguage = useRef(language);
  useEffect(() => {
    if (shownLanguage.current === language) return;
    shownLanguage.current = language;
    changed();
  }, [language, changed]);

  return (
    <div className="flex min-h-screen">
      {menuOpen && <button type="button" aria-label={t.app.closeMenu} className={BACKDROP} onClick={() => setMenuOpen(false)} />}
      <nav
        id={NAV_ID}
        aria-label={t.app.mainNav}
        // On a phone, whatever is chosen in the drawer is shown at once, not behind it; changing language is not a choice of view.
        onClick={event => {
          const chosen = (event.target as HTMLElement).closest('a, button');
          if (chosen && !chosen.closest('[role="group"]')) setMenuOpen(false);
        }}
        className={`on-navy ${navPlacement(menuOpen)} h-screen w-60 shrink-0 flex-col gap-6 overflow-y-auto bg-gradient-to-b from-navy to-navy-2 px-4 py-6 text-side-fg`}
      >
        <div className="flex items-center gap-3 px-1">
          <BrandMark size={40} />
          <span className="min-w-0" aria-hidden>
            <span className="block text-[17px] font-extrabold leading-5 tracking-tight text-white">{BRAND.name}</span>
            <span className="block text-xs leading-4 text-side-muted">{t.common.tagline}</span>
          </span>
          <span className="sr-only">
            {BRAND.name} · {t.common.tagline}
          </span>
        </div>
        <ul className="space-y-1">
          {NAV.map(item => (
            <li key={item.id}>
              {item.group && <p className="px-3 pb-2 pt-4 text-xs font-medium text-side-muted">{item.group}</p>}
              <button
                type="button"
                onClick={() => go(item.id)}
                aria-current={navOf(page) === item.id ? 'page' : undefined}
                className={`flex h-11 w-full items-center gap-3 rounded-md px-3 text-left text-data font-medium transition-colors ${navOf(page) === item.id ? 'bg-blue text-white shadow-[0_6px_16px_-6px_rgb(var(--blue)/0.8)]' : 'text-side-fg hover:bg-white/5 hover:text-white'}`}
              >
                {item.icon}
                {item.label}
              </button>
            </li>
          ))}
        </ul>
        <div className="mt-auto space-y-3">
          <div className="rounded-lg bg-white/5 p-3 text-xs leading-5 text-side-muted">
            <p className="flex items-center gap-2 font-semibold text-white">
              <ShieldCheck className="h-4 w-4 text-success-bright" aria-hidden />
              {t.app.engineActive}
            </p>
            {REGULATIONS}
            <br />
            {t.app.madridTime}
          </div>
          <label className="flex items-center gap-3 rounded-lg bg-white/5 p-3">
            <Avatar name={operator || null} toneKey="operator" />
            <span className="min-w-0 flex-1">
              <span className="block text-xs text-side-muted" title={t.app.declaredHint}>
                {t.app.assigningAsDeclared}
              </span>
              <input
                className="mt-0.5 h-8 w-full rounded-md border border-white/10 bg-navy/60 px-2 text-sm font-medium text-white placeholder:text-side-muted"
                aria-label={t.app.assigningAs}
                placeholder={t.app.yourName}
                maxLength={100}
                value={operator}
                onChange={event => {
                  setOperator(event.target.value);
                  setAssignedBy(event.target.value);
                }}
              />
            </span>
          </label>
          <LanguageSwitch tone="navy" className="px-1" />
          <div className="flex flex-wrap gap-x-4 gap-y-2 px-3 text-xs text-side-muted">
            <button type="button" className="underline hover:text-white" onClick={() => setTour('searching')}>
              {t.app.tour}
            </button>
            <a href={ENGINEERING_HASH} className="underline hover:text-white">
              {t.app.howItIsBuilt}
            </a>
            <a href={ABOUT_HASH} className="underline hover:text-white">
              {t.app.about}
            </a>
            <button
              type="button"
              className="underline hover:text-white"
              onClick={() => {
                setPrivacy(true);
                scrollTo({ top: 0 });
              }}
            >
              {t.app.privacy}
            </button>
          </div>
        </div>
      </nav>
      {/* While the drawer is open the page under it can be neither read nor reached with the keyboard (React 18 only passes `inert` as a string). */}
      <main className="min-w-0 flex-1" {...(menuOpen ? { inert: '' } : {})}>
        <TopBar page={page} onSearch={() => setPalette(true)} menuOpen={menuOpen} onMenu={() => setMenuOpen(true)} />
        <div className="gutter-x pb-10 pt-2">
          {error && (
            <div className="page mb-4">
              <ErrorBox>{t.app.masterFailed(error)}</ErrorBox>
            </div>
          )}
          {privacy && <PrivacyNotice onClose={() => setPrivacy(false)} />}
          {page === 'control' && (
            <ControlPage
              master={master}
              version={version}
              explained={explained}
              onAction={onAction}
              onNavigate={setPage}
              onOptimize={() => {
                setWantOptimizer(true);
                setPage('planning');
              }}
            />
          )}
          {page === 'planning' && (
            <PlanningPage
              master={master}
              version={version}
              explained={explained}
              openPanel={setPanel}
              onChanged={changed}
              onAllocated={onAllocated}
              onAssign={openAssign}
              startOptimizing={wantOptimizer}
              onOptimizerOpened={() => setWantOptimizer(false)}
              covered={covered}
            />
          )}
          {page === 'bookings' && <BookingsPage version={version} openPanel={setPanel} />}
          {page === 'drivers' && (
            <DriversPage master={master} version={version} onChanged={changed} onAllocated={onAllocated} onOpen={openDriver} />
          )}
          {page === 'engine' && (
            <EnginePage
              master={master}
              version={version}
              onOpenTrip={id => onAction({ kind: 'trip', tripId: id }, [])}
              onCatalog={groupKey => {
                setRulesGroup(groupKey ?? null);
                setPage('rules');
              }}
            />
          )}
          {page === 'rules' && (
            <RulesPage
              focusGroup={rulesGroup}
              onBack={() => {
                setRulesGroup(null);
                setPage('engine');
              }}
            />
          )}
          {page === 'audit' && <AuditPage />}
          {page === 'vehicles' && <VehiclesPage master={master} version={version} onOpen={openVehicle} />}
        </div>
      </main>
      {tour && (
        <Tour
          key={tour}
          startAt={tour}
          locName={master.locName}
          onTryIt={trip => {
            setTour(null);
            closeResource(true);
            setPanel({ bookingId: trip.BookingId, tripId: trip.Id });
          }}
          onEngine={() => {
            setTour(null);
            go('engine');
          }}
          onClose={() => setTour(null)}
        />
      )}
      <CommandPalette open={palette} onOpenChange={setPalette} views={VIEW_ITEMS} drivers={drivers} vehicles={vehicles} onPick={pick} />
      <ResourceModal
        target={resource}
        master={master}
        masterError={error}
        version={version}
        actions={{
          onTab: setResourceTab,
          onChanged: changed,
          onClose: () => closeResource(),
          onOpenTrip: (bookingId, tripId) => {
            closeResource(true);
            setPanel({ bookingId, tripId });
          },
          onOpenTripId: tripId => {
            closeResource(true);
            onAction({ kind: 'trip', tripId }, []);
          },
          onCatalog: groupKey => {
            closeResource(true);
            setRulesGroup(groupKey ?? null);
            setPage('rules');
          },
        }}
      />
      {assigning && (
        <AssignPeriodPanel
          target={assigning}
          hidden={covered}
          version={version}
          master={master}
          openPanel={setPanel}
          onChanged={changed}
          onAllocated={onAllocated}
          onClose={() => setAssigning(null)}
        />
      )}
      <BookingPanel
        target={panel}
        onClose={() => setPanel(null)}
        master={master}
        explained={explained}
        onChanged={changed}
        onAllocated={onAllocated}
      />
    </div>
  );
}

/** The edge shows only after scrolling; the state changes only when crossing the threshold, so it does not re-render on every pixel. */
function TopBar({ page, onSearch, menuOpen, onMenu }: { page: Page; onSearch: () => void; menuOpen: boolean; onMenu: () => void }) {
  const [scrolled, setScrolled] = useState(false);
  useEffect(() => {
    const onScroll = () => setScrolled(scrollY > 4);
    onScroll();
    addEventListener('scroll', onScroll, { passive: true });
    return () => removeEventListener('scroll', onScroll);
  }, []);
  const trail = [groupOf(navOf(page)), ...(page === 'rules' ? [labelOf('engine')] : []), labelOf(page)].filter(Boolean) as string[];
  const mac = isMac();
  return (
    <header className="topbar gutter-x" data-scrolled={scrolled}>
      <div className="page flex h-full items-center gap-4">
        <button
          type="button"
          onClick={onMenu}
          aria-label={t.app.openMenu}
          aria-expanded={menuOpen}
          aria-controls={NAV_ID}
          className="-ml-2 flex h-11 w-11 shrink-0 items-center justify-center rounded-md text-ink-2 hover:text-ink lg:hidden"
        >
          <Menu className="h-5 w-5" aria-hidden />
        </button>
        <ol aria-label={t.app.breadcrumb} className="flex min-w-0 flex-1 items-center gap-1.5 text-meta text-ink-3">
          {trail.map((crumb, index) => (
            <li key={crumb} className="flex min-w-0 items-center gap-1.5">
              {index > 0 && <ChevronRight className="h-3.5 w-3.5 shrink-0" aria-hidden />}
              <span
                className={`truncate ${index === trail.length - 1 ? 'font-medium text-ink-2' : ''}`}
                aria-current={index === trail.length - 1 ? 'location' : undefined}
              >
                {crumb}
              </span>
            </li>
          ))}
        </ol>
        <button
          type="button"
          onClick={onSearch}
          aria-keyshortcuts={mac ? 'Meta+K' : 'Control+K'}
          className="flex h-9 w-[min(360px,40vw)] items-center gap-2 rounded-md border border-line bg-surface/80 pl-3 pr-1.5 text-meta text-ink-3 shadow-1 transition-[box-shadow,color,border-color] duration-150 hover:shadow-2 hover:border-[rgb(var(--ink-3)/0.35)] hover:text-ink-2"
        >
          <Search className="h-4 w-4 shrink-0" aria-hidden />
          <span className="min-w-0 flex-1 truncate text-left">
            {t.app.goTo}<span className="hidden lg:inline"> {t.app.goToWhat}</span>
          </span>
          <span className="flex shrink-0 items-center gap-0.5" aria-hidden>
            <kbd className="kbd">{mac ? '⌘' : 'Ctrl'}</kbd>
            <kbd className="kbd">K</kbd>
          </span>
        </button>
      </div>
    </header>
  );
}
