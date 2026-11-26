import { read, write } from './storage';

export type Language = 'en' | 'es';

const LANGUAGES: readonly Language[] = ['en', 'es'];
const KEY = 'dispatchguard.language';
const LOCALES: Record<Language, string> = { en: 'en-GB', es: 'es-ES' };

const asLanguage = (text: string | null): Language | null => LANGUAGES.find(language => language === text) ?? null;

/** Of a link to the cover in one of them, as `#es`. */
export const languageOf = (hash: string): Language | null => asLanguage(hash.slice(1));

/** The language of the browser is not looked at: whoever has chosen none reads English (SPEC 0095 R1). */
export const initialLanguage = (hash: string, stored: string | null): Language => languageOf(hash) ?? asLanguage(stored) ?? 'en';

let current: Language = 'en';
const listeners = new Set<() => void>();
const loaders: ((language: Language) => Promise<void>)[] = [];

export const language = (): Language => current;

/** For dates and numbers; the time is always the one of Madrid. */
export const locale = (): string => LOCALES[current];

export const listOf = (parts: string[]): string => new Intl.ListFormat(locale(), { type: 'conjunction' }).format(parts);

export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => void listeners.delete(listener);
}

/** What has texts loads them before the language changes, so that nothing is ever painted without them. */
export async function withTexts(load: (language: Language) => Promise<void>): Promise<void> {
  await load(current);
  loaders.push(load);
}

/** Without remembering it: the start of a visit, and the tests. */
export async function setLanguage(next: Language): Promise<void> {
  await Promise.all(loaders.map(load => load(next)));
  current = next;
  for (const listener of listeners) listener();
}

export const startLanguage = (): Promise<void> => {
  const initial = initialLanguage(location.hash, read(KEY));
  if (languageOf(location.hash)) write(KEY, initial);
  return setLanguage(initial);
};

/** Remembered; the address stops asking for another one. */
export function chooseLanguage(next: Language): Promise<void> {
  write(KEY, next);
  if (languageOf(location.hash)) history.replaceState(history.state, '', location.pathname + location.search);
  return setLanguage(next);
}
