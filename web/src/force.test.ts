import { describe, expect, it } from 'vitest';
import { forceError, laterServicesText, MAX_REASON, MIN_REASON } from './force';

describe('forceError', () => {
  it('asks for a reason', () => expect(forceError('   ', 'Lucía')).toMatch(/al menos 10/));
  it('9 characters after trimming → error; 10 → ok', () => {
    expect(forceError(` ${'a'.repeat(MIN_REASON - 1)} `, 'Lucía')).not.toBeNull();
    expect(forceError(` ${'a'.repeat(MIN_REASON)} `, 'Lucía')).toBeNull();
  });
  it('upper limit', () => {
    expect(forceError('a'.repeat(MAX_REASON), 'Lucía')).toBeNull();
    expect(forceError('a'.repeat(MAX_REASON + 1), 'Lucía')).toMatch(/como mucho/);
  });
  it('asks for a name', () => expect(forceError('Sin otro conductor libre', ' ')).toMatch(/quién/));
});

describe('laterServicesText', () => {
  const laterService = (TripId: number, Article = 'Art. 7') => ({ TripId, Start: '2026-11-28T12:00:00', Article, Rule: 'x', Message: 'm' });
  it('nothing without later services', () => expect(laterServicesText(9, [])).toBeNull());
  it('counts services, not rules', () =>
    expect(laterServicesText(9, [laterService(5), laterService(5, 'Art. 6.1')])).toMatch(
      /^1 servicio posterior .*«Forzado por el servicio #9»/,
    ));
  it('plural', () =>
    expect(laterServicesText(9, [laterService(5), laterService(6)])).toMatch(/^2 servicios posteriores .*«Forzado por el servicio #9»/));
});
