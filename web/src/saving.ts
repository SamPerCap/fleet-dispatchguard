/**
 * Counts finished requests whether they succeed or not (a lost response may have been stored). Whoever shows figures calculated from
 * the assignments hides them meanwhile and requests them again afterwards.
 */
export interface SavingState {
  pending: number;
  settled: number;
}

export function createSaving() {
  let state: SavingState = { pending: 0, settled: 0 };
  const listeners = new Set<() => void>();
  const set = (next: SavingState) => {
    state = next;
    listeners.forEach(l => l());
  };
  return {
    state: (): SavingState => state,
    subscribe(listener: () => void): () => void {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    async track<T>(run: () => Promise<T>): Promise<T> {
      set({ ...state, pending: state.pending + 1 });
      try {
        return await run();
      } finally {
        set({ pending: state.pending - 1, settled: state.settled + 1 });
      }
    },
  };
}

/** Shared by the whole web: fed by the calls that assign (`api.allocate`, `recommendations.apply`) and by those of the agenda. */
export const saving = createSaving();
