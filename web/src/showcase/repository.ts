// The public repository does not exist yet: SPEC 0099 creates it and fills this line. While it is empty the pages say that the
// code will be published and link nowhere, so a reader never meets a broken link (SPEC 0096 R5).
export const REPOSITORY_URL = '';

/** Where a file of the repository can be read, or null while there is no public repository. */
export function sourceUrl(path: string, repository = REPOSITORY_URL): string | null {
  if (!repository) return null;
  return `${repository.replace(/\/+$/, '')}/blob/master/${path}`;
}
