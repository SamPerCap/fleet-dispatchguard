import { t } from './i18n/t';
import { useState } from 'react';
import { ApiError } from './api';
import { failureTone } from './retry';

/** Reports changes on an API rejection too: what is on screen was probably out of date. */
export function useSave(onChanged: () => void) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const run = async (save: () => Promise<unknown>, onDone: () => void, onFailed?: (cause: Error) => void) => {
    setBusy(true);
    setError(null);
    try {
      await save();
      onDone();
    } catch (cause) {
      setError(cause as Error);
      onFailed?.(cause as Error);
      if (cause instanceof ApiError && (cause.status === 404 || cause.status === 409)) onChanged();
    } finally {
      setBusy(false);
    }
  };
  return { busy, error, run, clearError: () => setError(null) };
}

export function SaveError({ error, busy, onRetry }: { error: Error; busy: boolean; onRetry: () => void }) {
  const retryable = error instanceof ApiError && error.retryable;
  return (
    <div role="alert" className={`flex flex-wrap items-center gap-3 rounded-md px-3 py-2 text-meta font-medium ${failureTone(retryable)}`}>
      <span className="min-w-0 flex-1">{error.message}</span>
      {retryable && (
        <button type="button" className="btn btn-sm" disabled={busy} onClick={onRetry}>
          {busy ? t.common.retrying : t.common.retry}
        </button>
      )}
    </div>
  );
}
