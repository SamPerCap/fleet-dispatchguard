import { describe, expect, it } from 'vitest';
import { PAGE_IDS } from './pageIds';
import { currentAnchorOf, placeOf } from './route';

describe('what an address shows', () => {
  it.each(['', '#', '#es', '#en', '#something-else'])('«%s» is the cover', hash => {
    expect(placeOf(hash, '')).toBe('cover');
  });

  it.each(PAGE_IDS)('#%s skips the cover', page => {
    expect(placeOf(`#${page}`, '')).toBe('application');
  });

  it.each(['?conductor=3', '?vehiculo=2&pestana=agenda'])('the link of a card skips the cover: %s', search => {
    expect(placeOf('', search)).toBe('application');
  });

  it('the code of a link does not skip the cover', () => {
    expect(placeOf('', '?ref=acme')).toBe('cover');
  });

  it('the engineering page and the cover in a language can be reached with a card in the address', () => {
    expect(placeOf('#how-it-is-built', '?conductor=3')).toBe('engineering');
    expect(placeOf('#about', '?conductor=3')).toBe('about');
    expect(placeOf('#en', '?conductor=3')).toBe('cover');
  });

  it('the old Spanish anchor still opens the engineering page and is rewritten to the English one', () => {
    expect(placeOf('#como-esta-hecho', '')).toBe('engineering');
    expect(currentAnchorOf('#como-esta-hecho')).toBe('#how-it-is-built');
    expect(currentAnchorOf('#how-it-is-built')).toBeNull();
  });
});
