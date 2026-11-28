import { languageOf } from './language';
import { isPage } from './pageIds';
import { readResource } from './resourceUrl';

export type Place = 'cover' | 'engineering' | 'about' | 'application';

export const ENGINEERING_HASH = '#how-it-is-built';
/** Links already shared with the Spanish anchor keep working (SPEC 0100 R5). */
const OLD_ENGINEERING_HASH = '#como-esta-hecho';
export const ABOUT_HASH = '#about';
export const START_HASH = '#control';
/** The cover's shortest way to the key moment: the tool opens with the tour already looking for an illegal case (SPEC 0104). */
export const BREAK_THE_LAW_HASH = '#break-the-law';

/** A link to something of the application goes straight to it (SPEC 0093 R1). */
export function placeOf(hash: string, search: string): Place {
  if (hash === ENGINEERING_HASH || hash === OLD_ENGINEERING_HASH) return 'engineering';
  if (hash === ABOUT_HASH) return 'about';
  if (hash === BREAK_THE_LAW_HASH) return 'application';
  if (languageOf(hash)) return 'cover';
  return isPage(hash.slice(1)) || readResource(search) ? 'application' : 'cover';
}

/** Rewrites an old anchor in place, so that the address shown is the current one and Back does not gain a step. */
export function currentAnchorOf(hash: string): string | null {
  return hash === OLD_ENGINEERING_HASH ? ENGINEERING_HASH : null;
}

/** Whoever asked to break the law skips the offer; coming from the cover only offers it; a shared link to a page brings none. */
export function tourStartOf(hash: string, comesFromTheCover: boolean): 'offer' | 'searching' | null {
  if (hash === BREAK_THE_LAW_HASH) return 'searching';
  return comesFromTheCover ? 'offer' : null;
}
