import { languageOf } from './language';
import { isPage } from './pageIds';
import { readResource } from './resourceUrl';

export type Place = 'cover' | 'engineering' | 'about' | 'application';

export const ENGINEERING_HASH = '#como-esta-hecho';
export const ABOUT_HASH = '#about';
export const START_HASH = '#control';

/** A link to something of the application goes straight to it (SPEC 0093 R1). */
export function placeOf(hash: string, search: string): Place {
  if (hash === ENGINEERING_HASH) return 'engineering';
  if (hash === ABOUT_HASH) return 'about';
  if (languageOf(hash)) return 'cover';
  return isPage(hash.slice(1)) || readResource(search) ? 'application' : 'cover';
}
