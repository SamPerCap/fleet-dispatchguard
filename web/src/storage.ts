// If the browser does not allow storage, the preference lasts only for this visit.
import type { Scope } from './api';

export const read = (key: string) => {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
};
export const write = (key: string, value: string) => {
  try {
    localStorage.setItem(key, value);
  } catch {
    /* Only for this visit. */
  }
};

const BASE_KEY = 'dispatchguard.baseId';
export const readScope = (): Scope => {
  const stored = read(BASE_KEY);
  const parsed = Number(stored);
  return stored && stored !== 'all' && Number.isInteger(parsed) && parsed > 0 ? parsed : 'all';
};
export const writeScope = (scope: Scope) => write(BASE_KEY, String(scope));
