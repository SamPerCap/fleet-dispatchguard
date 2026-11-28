import type { Place } from '../route';
import { read, write } from '../storage';

const AUTHOR_KEY = 'dispatchguard.author';

const PAGE_OF: Record<Place, string> = { cover: 'Cover', engineering: 'Engineering', about: 'About', application: 'Application' };

interface Options {
  send: (body: string) => void;
  storage: { read: (key: string) => string | null; write: (key: string, value: string) => void };
}

/**
 * The only request the showcase makes (SPEC 0103, which relaxes SPEC 0093 R1): it tells the author which page was opened. It needs
 * no session and wakes no database; the server sends at most one notice per visitor every 30 minutes.
 */
export function createOpenedNotice({ send, storage }: Options) {
  let announced = false;
  return {
    /** Once per page load: moving around the site afterwards is the same visit. */
    announce: (place: Place) => {
      if (announced || storage.read(AUTHOR_KEY)) return;
      announced = true;
      try {
        send(JSON.stringify({ Page: PAGE_OF[place] }));
      } catch {
        /* A visit never depends on the notice. */
      }
    },
    /** Set once the audit key is accepted: the author's own visits are not news. */
    markAuthor: () => storage.write(AUTHOR_KEY, '1'),
  };
}

export const openedNotice = createOpenedNotice({
  // keepalive: the visitor may close the tab right after opening it, and the notice should still leave.
  send: body =>
    void fetch('/api/visits/opened', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body, keepalive: true }).catch(() => {}),
  storage: { read, write },
});
