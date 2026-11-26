import { describe, expect, it } from 'vitest';
import { initialLanguage } from './language';

describe('the language of a visit (SPEC 0095 R1)', () => {
  it('is English for whoever has chosen none, whatever the browser prefers', () => {
    expect(initialLanguage('', null)).toBe('en');
    expect(initialLanguage('#control', null)).toBe('en');
  });

  it('is the one chosen before', () => {
    expect(initialLanguage('', 'es')).toBe('es');
    expect(initialLanguage('', 'en')).toBe('en');
  });

  it('is the one of the link to the cover, over the one chosen before', () => {
    expect(initialLanguage('#es', 'en')).toBe('es');
    expect(initialLanguage('#en', 'es')).toBe('en');
  });

  it.each(['fr', 'es-ES', '', 'ES'])('a stored «%s» is not a choice', stored => {
    expect(initialLanguage('', stored)).toBe('en');
  });
});
