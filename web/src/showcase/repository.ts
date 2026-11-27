// An empty value makes the pages say that the code will be published and link nowhere, so a reader never meets a broken link.
export const REPOSITORY_URL = 'https://github.com/SamPerCap/fleet-dispatchguard';

/** Where a file of the repository can be read, or null while there is no public repository. */
export function sourceUrl(path: string, repository = REPOSITORY_URL): string | null {
  if (!repository) return null;
  return `${repository.replace(/\/+$/, '')}/blob/master/${path}`;
}
