import { t } from './i18n/t';
import { normalize } from './palette';

/** With thousands of items, rendering every match would freeze the form: the rest is reached by typing more. */
export const MAX_OPTIONS = 20;

export interface PickerItem {
  id: number;
  label: string;
  /** Shown but not searched: it may be a whole sentence of the engine. */
  detail: string;
  tags?: string[];
  /** Searched but not shown. */
  keywords?: string;
  /** Cannot be chosen: it only appears when searched, and `detail` says why. */
  blocked?: boolean;
}

export interface PickerMatches {
  /** Only without a query. */
  suggested: PickerItem[];
  matches: PickerItem[];
  blocked: PickerItem[];
  /** Including those the cap leaves out. */
  total: number;
}

interface Entry {
  item: PickerItem;
  label: string;
  labelWords: string[];
  text: string;
}

const searchable = (text: string) =>
  normalize(text)
    .replace(/[^\p{L}\p{N}]+/gu, ' ')
    .trim();

function toEntry(item: PickerItem): Entry {
  const label = searchable(item.label);
  return { item, label, labelWords: label.split(' '), text: searchable(`${item.label} ${item.keywords ?? ''}`) };
}

// Shared by every field of a form that receives the same list.
const entriesByList = new WeakMap<PickerItem[], Entry[]>();
function entriesOf(items: PickerItem[]): Entry[] {
  let entries = entriesByList.get(items);
  if (!entries) {
    entries = items.map(toEntry);
    entriesByList.set(items, entries);
  }
  return entries;
}

/** 0 = the label starts with the query; 1 = a word of the label starts with a word of the query; 2 = the rest. */
function rank(entry: Entry, query: string, words: string[]): number {
  if (entry.label.startsWith(query)) return 0;
  return entry.labelWords.some(labelWord => words.some(word => labelWord.startsWith(word))) ? 1 : 2;
}

function withoutQuery(items: PickerItem[], suggestedIds: number[], max: number): PickerMatches {
  const choosable = items.filter(item => !item.blocked);
  const byId = new Map(choosable.map(item => [item.id, item]));
  const suggested = [...new Set(suggestedIds)].flatMap(id => byId.get(id) ?? []).slice(0, max);
  const taken = new Set(suggested.map(item => item.id));
  const matches = choosable.filter(item => !taken.has(item.id)).slice(0, max - suggested.length);
  return { suggested, matches, blocked: [], total: choosable.length };
}

/** Equal matches keep the order of `items`. The ones that cannot be chosen go last and only take the room the others leave. */
export function searchItems(items: PickerItem[], query: string, suggestedIds: number[] = [], max = MAX_OPTIONS): PickerMatches {
  const needle = searchable(query);
  if (!needle) return withoutQuery(items, suggestedIds, max);
  const words = needle.split(' ');
  const ranked: PickerItem[][] = [[], [], []];
  for (const entry of entriesOf(items))
    if (words.every(word => entry.text.includes(word))) ranked[rank(entry, needle, words)].push(entry.item);
  const all = ranked.flat();
  const matches = all.filter(item => !item.blocked).slice(0, max);
  const blocked = all.filter(item => item.blocked).slice(0, max - matches.length);
  return { suggested: [], matches, blocked, total: all.length };
}

export function optionsNote(shown: number, total: number, query: string, plural: string): string | null {
  if (shown >= total) return null;
  return t.picker.shownOf(shown, total, plural, query.trim() ? t.picker.keepTyping : t.picker.type);
}

/** `current` is -1 when no option is active. null: the key does not move through the list. */
export function optionAfterKey(key: string, current: number, count: number): number | null {
  if (count === 0) return null;
  const last = count - 1;
  switch (key) {
    case 'ArrowDown':
      return Math.min(last, current + 1);
    case 'ArrowUp':
      return current < 0 ? last : Math.max(0, current - 1);
    case 'Home':
      return 0;
    case 'End':
      return last;
    default:
      return null;
  }
}
