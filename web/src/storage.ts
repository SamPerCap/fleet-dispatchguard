// If the browser does not allow storage, the preference lasts only for this visit.
import type { Scope } from './api';

/** What was kept under the names before the rename (SPEC 0094 R3); the new key wins, even if it is empty. */
const PREVIOUS_KEYS: Record<string, string> = {
  'dispatchguard.assignedBy': 'lof.assignedBy',
  'dispatchguard.baseId': 'lof.baseId',
  'dispatchguard.stripHours': 'lof.stripHours',
  'dispatchguard.stripMode': 'lof.stripMode',
  'dispatchguard.welcomeDismissed': 'betterbus.welcomeDismissed',
};

export const read = (key: string) => {
  try {
    const previousKey = PREVIOUS_KEYS[key];
    return localStorage.getItem(key) ?? (previousKey ? localStorage.getItem(previousKey) : null);
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
