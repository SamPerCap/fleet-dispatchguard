import { describe, expect, it } from 'vitest';
import { failureText, failureTone, isRetryable } from './retry';

describe('wait run out', () => {
  it('only the 503 that says so is retryable', () => {
    expect(isRetryable(503, { retryable: true })).toBe(true);
    expect(isRetryable(503, {})).toBe(false); // a 503 from the proxy, not from the API
    expect(isRetryable(503, undefined)).toBe(false);
    expect(isRetryable(409, { retryable: true })).toBe(false); // an overlap is never retried as it is
    expect(isRetryable(422, { retryable: 'true' })).toBe(false);
  });
  it('amber if it can be retried, red if it is a rejection', () => {
    expect(failureTone(true)).toContain('warning');
    expect(failureTone(false)).toContain('critical');
  });
  it('without violations it shows the whole message; with them, the title and each one', () => {
    expect(
      failureText({ title: 'Otra operación en curso', message: 'Otra operación en curso: No se ha guardado nada', violations: [] }),
    ).toBe('Otra operación en curso: No se ha guardado nada');
    expect(failureText({ title: 'No cumple', message: 'No cumple: a', violations: ['a', 'b'] })).toBe('No cumple · a · b');
  });
});
