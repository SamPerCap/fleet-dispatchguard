/**
 * Half-filled forms, by driver or vehicle, until the page is reloaded: following an affected service closes the card, and what was
 * typed must be there on coming back.
 */
export function createDrafts<T>() {
  const drafts = new Map<string, T>();
  return {
    read: (key: string): T | null => drafts.get(key) ?? null,
    write(key: string, draft: T | null) {
      if (draft) drafts.set(key, draft);
      else drafts.delete(key);
    },
  };
}
