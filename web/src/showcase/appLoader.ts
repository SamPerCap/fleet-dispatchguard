export interface ReloadMark {
  isSet: () => boolean;
  set: () => void;
  clear: () => void;
}

/**
 * After a deployment, a page that was already open asks for a file of the previous version, which is gone. Reloading brings the new
 * one; only once, so that a server that is down does not make it reload for ever.
 */
export function createAppLoader<T>(load: () => Promise<T>, reload: () => void, reloaded: ReloadMark) {
  return async (): Promise<T> => {
    try {
      const application = await load();
      reloaded.clear();
      return application;
    } catch (failure) {
      if (reloaded.isSet()) {
        reloaded.clear();
        throw failure;
      }
      reloaded.set();
      reload();
      return new Promise<T>(() => {});
    }
  };
}

const KEY = 'dispatchguard.reloadedForTheApplication';

/** Without storage it counts as already reloaded. */
export const reloadMarkOfTheTab: ReloadMark = {
  isSet: () => {
    try {
      return sessionStorage.getItem(KEY) !== null;
    } catch {
      return true;
    }
  },
  set: () => sessionStorage.setItem(KEY, '1'),
  clear: () => {
    try {
      sessionStorage.removeItem(KEY);
    } catch {
      /* Nothing was stored. */
    }
  },
};
