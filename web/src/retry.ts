/**
 * Another operation held the driver, the vehicle or the service and the wait ran out. Not a rejection (nothing was evaluated or
 * stored): it is shown in amber and can be tried again as it is (ADR 0018).
 */
export const isRetryable = (status: number, body: { retryable?: unknown } | null | undefined): boolean =>
  status === 503 && body?.retryable === true;

export const failureTone = (retryable: boolean): string =>
  retryable ? 'bg-warning-soft text-warning-ink' : 'bg-critical-soft text-critical-ink';

export const failureText = (failure: { title: string; message: string; violations: string[] }): string =>
  [failure.violations.length ? failure.title : failure.message, ...failure.violations].join(' · ');
