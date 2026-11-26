import { t } from './i18n/t';
import { addDays, startOfDay } from './gantt';

export type PaletteKind = 'view' | 'booking' | 'driver' | 'vehicle';
export const PALETTE_KINDS: PaletteKind[] = ['view', 'booking', 'driver', 'vehicle'];
export const PALETTE_GROUP: Record<PaletteKind, string> = {
  get view() { return t.command.views; },
  get booking() { return t.app.bookings; },
  get driver() { return t.common.drivers; },
  get vehicle() { return t.common.vehicles; },
};
/** So that no kind hides the others. */
export const PER_KIND = 6;

export interface PaletteItem {
  kind: PaletteKind;
  /** The page, or the numeric Id of the booking, driver or vehicle. */
  target: string | number;
  label: string;
  detail?: string;
  /** Also searched but never shown (e.g. the name of the view in other words). */
  keywords?: string;
}

export const normalize = (text: string) => text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/\s+/g, ' ').trim();

/** 0 = the label starts with the query; 1 = a word of the label starts with the first word; 2 = the rest. */
function rank(item: PaletteItem, query: string, first: string): number {
  const label = normalize(item.label);
  if (label.startsWith(query)) return 0;
  return label.split(' ').some(w => w.startsWith(first)) ? 1 : 2;
}

/**
 * Groups are ordered by their best match (a driver whose name starts with the query goes before bookings that only contain it in
 * their description). Without a query, only the views: the rest would be an endless list.
 */
export function searchPalette(items: PaletteItem[], query: string, perKind = PER_KIND): PaletteItem[] {
  const needle = normalize(query);
  if (!needle) return items.filter(i => i.kind === 'view');
  const words = needle.split(' ');
  const groups = PALETTE_KINDS.map((kind, order) => {
    const matches = items
      .filter(i => i.kind === kind)
      .map(item => ({ item, text: normalize(`${item.label} ${item.detail ?? ''} ${item.keywords ?? ''}`) }))
      .filter(entry => words.every(w => entry.text.includes(w)))
      .map(({ item }) => ({ item, rank: rank(item, needle, words[0]) }))
      .sort((a, b) => a.rank - b.rank || a.item.label.localeCompare(b.item.label, 'es'))
      .slice(0, perKind);
    return { order, best: matches.length ? matches[0].rank : Infinity, items: matches.map(m => m.item) };
  });
  return groups
    .filter(g => g.items.length)
    .sort((a, b) => a.best - b.best || a.order - b.order)
    .flatMap(g => g.items);
}

/** In the order they appear in the results. */
export const kindsIn = (results: PaletteItem[]): PaletteKind[] => [...new Set(results.map(r => r.kind))];

/** 31 days: the cap of `/api/bookings`. */
export function bookingWindow(now: Date): { from: Date; to: Date } {
  const today = startOfDay(now);
  return { from: addDays(today, -15), to: addDays(today, 16) };
}
