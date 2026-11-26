import { t } from './i18n/t';
import * as Dialog from '@radix-ui/react-dialog';
import { BusFront, ClipboardList, CornerDownLeft, LayoutGrid, Search, User, type LucideIcon } from 'lucide-react';
import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { api } from './api';
import { parseApiDate, TIME_ZONE } from './gantt';
import { bookingWindow, kindsIn, PALETTE_GROUP, searchPalette, type PaletteItem, type PaletteKind } from './palette';
import type { Booking, Driver, Vehicle } from './types';
import { locale } from './language';

const KIND_ICON: Record<PaletteKind, LucideIcon> = { view: LayoutGrid, booking: ClipboardList, driver: User, vehicle: BusFront };
const dayText = (date: Date) => date.toLocaleDateString(locale(), { day: 'numeric', month: 'short', timeZone: TIME_ZONE });
const whenText = (date: Date) =>
  date.toLocaleString(locale(), { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', timeZone: TIME_ZONE });

/** Only to label the shortcut: it works with both keys on any system. */
export const isMac = () => typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);

/** Combobox + listbox pattern: arrows move, Enter chooses, Esc closes and gives focus back. */
export function CommandPalette({
  open,
  onOpenChange,
  views,
  drivers,
  vehicles,
  onPick,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  views: PaletteItem[];
  drivers: Driver[];
  vehicles: Vehicle[];
  onPick: (item: PaletteItem) => void;
}) {
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const [bookings, setBookings] = useState<{ list: Booking[]; error: string | null; loading: boolean }>({
    list: [],
    error: null,
    loading: false,
  });
  const picked = useRef(false);
  // Where the focus was on opening (read before the dialog moves it): closing without choosing returns there, also if the palette
  // opened on top of a panel (Radix would leave it on <body>).
  const returnTo = useRef<HTMLElement | null>(null);
  const wasOpen = useRef(false);
  if (open && !wasOpen.current) returnTo.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  wasOpen.current = open;
  const list = useRef<HTMLDivElement>(null);
  const listId = useId();
  const win = useMemo(() => bookingWindow(new Date()), [open]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!open) return;
    setQuery('');
    setActive(0);
    picked.current = false;
    let alive = true;
    setBookings(b => ({ ...b, loading: true, error: null }));
    api
      .bookings(win.from, win.to)
      .then(list => {
        if (alive) setBookings({ list, error: null, loading: false });
      })
      .catch(cause => {
        if (alive) setBookings({ list: [], error: (cause as Error).message, loading: false });
      });
    return () => {
      alive = false;
    };
  }, [open, win]);

  const items = useMemo<PaletteItem[]>(
    () => [
      ...views,
      ...bookings.list.map(booking => ({
        kind: 'booking' as const,
        target: booking.Id,
        label: booking.CustomerName,
        detail: [`#${booking.Id}`, booking.Description, booking.Start ? whenText(parseApiDate(booking.Start)) : null]
          .filter(Boolean)
          .join(' · '),
      })),
      ...drivers.map(driver => ({
        kind: 'driver' as const,
        target: driver.Id,
        label: `${driver.FirstName} ${driver.LastName}`,
        detail: [driver.IdentificationNumber, driver.IsActive ? null : 'inactivo', driver.IsExternal ? 'externo' : null]
          .filter(Boolean)
          .join(' · '),
      })),
      ...vehicles.map(vehicle => ({
        kind: 'vehicle' as const,
        target: vehicle.Id,
        label: vehicle.LicensePlate,
        detail: [`${vehicle.Brand} ${vehicle.Model}`.trim(), `${vehicle.SeatingCapacity} plazas`, vehicle.IsActive ? null : 'inactivo']
          .filter(Boolean)
          .join(' · '),
      })),
    ],
    [views, bookings.list, drivers, vehicles],
  );
  const results = useMemo(() => searchPalette(items, query), [items, query]);
  const current = Math.min(active, Math.max(0, results.length - 1));
  const optionId = (optionIndex: number) => `${listId}-o${optionIndex}`;

  useEffect(() => {
    setActive(0);
  }, [query]);
  useEffect(() => {
    list.current?.querySelector(`[id="${CSS.escape(optionId(current))}"]`)?.scrollIntoView({ block: 'nearest' });
  }, [current]); // eslint-disable-line react-hooks/exhaustive-deps

  const choose = (item: PaletteItem | undefined) => {
    if (!item) return;
    picked.current = true;
    onOpenChange(false);
    onPick(item);
  };
  const onKey = (event: KeyboardEvent) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActive(Math.min(results.length - 1, current + 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActive(Math.max(0, current - 1));
    } else if (event.key === 'Home' && results.length) {
      event.preventDefault();
      setActive(0);
    } else if (event.key === 'End' && results.length) {
      event.preventDefault();
      setActive(results.length - 1);
    } else if (event.key === 'Enter') {
      event.preventDefault();
      choose(results[current]);
    }
  };

  const range = `${dayText(win.from)} – ${dayText(new Date(win.to.getTime() - 1))}`;
  const note =
    query.trim() && bookings.loading
      ? t.command.searchingBookings
      : bookings.error
        ? t.command.bookingsFailed(bookings.error)
        : null;
  let index = -1;

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fade-in fixed inset-0 z-50 bg-navy/25 backdrop-blur-[2px]" />
        <Dialog.Content
          aria-describedby={undefined}
          onCloseAutoFocus={event => {
            // If the chosen target opens a panel, the panel keeps the focus; otherwise it returns where it was.
            event.preventDefault();
            const back = () => {
              if (returnTo.current?.isConnected && (!picked.current || document.activeElement === document.body)) returnTo.current.focus();
            };
            if (picked.current) requestAnimationFrame(back);
            else back();
          }}
          className="palette-pop fixed inset-x-0 top-[12vh] z-50 mx-auto flex max-h-[min(560px,76vh)] w-[min(640px,calc(100vw-32px))] flex-col overflow-hidden rounded-lg bg-surface shadow-pop"
        >
          <Dialog.Title className="sr-only">{t.command.title}</Dialog.Title>
          <div className="flex items-center gap-3 border-b border-line px-4">
            <Search className="h-5 w-5 shrink-0 text-ink-3" aria-hidden />
            <input
              autoFocus
              role="combobox"
              aria-expanded
              aria-controls={listId}
              aria-autocomplete="list"
              aria-activedescendant={results.length ? optionId(current) : undefined}
              aria-label={t.command.label}
              placeholder={t.command.placeholder}
              className="h-14 min-w-0 flex-1 bg-transparent text-module text-ink outline-none placeholder:text-ink-3"
              value={query}
              onChange={e => setQuery(e.target.value)}
              onKeyDown={onKey}
            />
            <kbd className="kbd">{'Esc'}</kbd>
          </div>
          <div ref={list} id={listId} role="listbox" aria-label={t.command.results} className="min-h-0 flex-1 overflow-y-auto p-2">
            {kindsIn(results).map(kind => {
              const group = results.filter(r => r.kind === kind);
              return (
                <div key={kind} role="group" aria-labelledby={`${listId}-${kind}`} className="pb-1">
                  <p id={`${listId}-${kind}`} className="px-3 pb-1 pt-2 text-xs font-medium text-ink-3">
                    {PALETTE_GROUP[kind]}
                    {kind === 'booking' && <span className="font-normal"> · {range}</span>}
                  </p>
                  {group.map(item => {
                    const optionIndex = ++index;
                    const Icon = KIND_ICON[kind];
                    const isCurrent = optionIndex === current;
                    return (
                      <div
                        key={`${kind}-${item.target}`}
                        id={optionId(optionIndex)}
                        role="option"
                        aria-selected={isCurrent}
                        onMouseMove={() => {
                          if (!isCurrent) setActive(optionIndex);
                        }}
                        onClick={() => choose(item)}
                        className={`flex cursor-pointer items-center gap-3 rounded-md px-3 py-2 ${isCurrent ? 'bg-blue-soft' : ''}`}
                      >
                        <span
                          aria-hidden
                          className={`grid h-8 w-8 shrink-0 place-items-center rounded-full ${isCurrent ? 'bg-surface text-blue-ink' : 'bg-muted text-ink-2'}`}
                        >
                          <Icon className="h-4 w-4" />
                        </span>
                        <span className="min-w-0 flex-1">
                          <span className={`block truncate font-medium ${isCurrent ? 'text-blue-ink' : 'text-ink'}`}>{item.label}</span>
                          {item.detail && <span className="block truncate text-xs text-ink-2">{item.detail}</span>}
                        </span>
                        {isCurrent && <CornerDownLeft className="h-4 w-4 shrink-0 text-blue-ink" aria-hidden />}
                      </div>
                    );
                  })}
                </div>
              );
            })}
            {results.length === 0 && (
              <p className="px-3 py-8 text-center text-meta text-ink-2" role="status">
                {bookings.loading ? t.common.searching : <>{t.common.noMatch(query.trim())}</>}
              </p>
            )}
          </div>
          <footer className="flex flex-wrap items-center gap-x-4 gap-y-1 border-t border-line bg-sunken px-4 py-2 text-xs text-ink-2">
            <span className="inline-flex items-center gap-1">
              <kbd className="kbd">↑</kbd>
              <kbd className="kbd">↓</kbd>{t.command.move}
            </span>
            <span className="inline-flex items-center gap-1">
              <kbd className="kbd">↵</kbd>{t.command.open}
            </span>
            {note && (
              <span role="status" className={bookings.error ? 'text-critical-ink' : ''}>
                {note}
              </span>
            )}
          </footer>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
