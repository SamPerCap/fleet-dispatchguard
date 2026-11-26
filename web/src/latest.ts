/**
 * Guard against stale responses: each load calls `begin()` and, when it finishes, applies its result only if the returned check
 * still holds. Otherwise a late response would undo the navigation.
 */
export function createLatestGuard() {
  let seq = 0;
  return {
    begin(): () => boolean {
      const id = ++seq;
      return () => id === seq;
    },
  };
}
