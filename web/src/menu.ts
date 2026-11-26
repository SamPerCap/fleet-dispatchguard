// Below `lg` the 240 px side menu would leave a phone 120 px of page, so there it is a drawer that covers the page only while it
// is open; from `lg` it is the fixed column it always was (SPEC 0096 R7).
export const NAV_ID = 'main-nav';

export function navPlacement(open: boolean): string {
  const narrow = open ? 'fixed inset-y-0 left-0 z-40 flex shadow-2' : 'hidden';
  return `${narrow} lg:sticky lg:top-0 lg:z-auto lg:flex lg:shadow-none`;
}

/** The page under an open drawer: a click on it closes the drawer. Never there from `lg`. */
export const BACKDROP = 'fixed inset-0 z-30 bg-navy/40 lg:hidden';
