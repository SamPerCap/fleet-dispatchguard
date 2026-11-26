import { describe, expect, it } from 'vitest';
import { en } from './en';
import { es } from './es';
import { LABELS_IN_ENGLISH } from '../visitLabels';

// eslint-disable-next-line @typescript-eslint/no-explicit-any -- each text has parameters of its own; here only their number counts.
type Entry = string | ((...shown: any[]) => string);
type Catalogue = Record<string, Record<string, Entry>>;

const entries = (catalogue: Catalogue) =>
  Object.entries(catalogue).flatMap(([section, texts]) => Object.entries(texts).map(([key, text]) => [`${section}.${key}`, text] as const));

const english = new Map<string, Entry>(entries(en as unknown as Catalogue));
const spanish = new Map<string, Entry>(entries(es as unknown as Catalogue));

/** 1 and 2: a plural shows both of its forms. */
const sampleOf = (text: Entry, one: boolean) =>
  typeof text === 'string' ? text : text(...Array.from({ length: text.length }, (_, i) => (one ? 1 : 20 + i)));

const figures = (text: string) => (text.match(/\d+([.,]\d+)?/g) ?? []).map(figure => figure.replace(',', '.')).sort();

describe('the catalogue of the web (SPEC 0095 CA1)', () => {
  it('has the same texts in both languages', () => {
    expect([...spanish.keys()].sort()).toEqual([...english.keys()].sort());
  });

  it('every text has the same parameters, and words, in both', () => {
    for (const key of english.keys()) expectTheSameShape(key);
  });

  it('every text with figures shows the same ones in both', () => {
    for (const [key, text] of english)
      if (typeof text === 'function')
        for (const one of [true, false]) expect(figures(sampleOf(spanish.get(key)!, one)), key).toEqual(figures(sampleOf(text, one)));
  });

  it('the controls of the visit are recognised in English too', () => {
    const shown = [...english.values()].flatMap(text => [sampleOf(text, true), sampleOf(text, false)]);
    expect(LABELS_IN_ENGLISH.filter(label => !shown.some(text => text.startsWith(label)))).toEqual([]);
  });
});

function expectTheSameShape(key: string) {
  const [inEnglish, inSpanish] = [english.get(key)!, spanish.get(key)!];
  expect(typeof inSpanish).toBe(typeof inEnglish);
  if (typeof inEnglish === 'function' && typeof inSpanish === 'function') expect(inSpanish.length).toBe(inEnglish.length);
  for (const one of [true, false]) {
    expect(sampleOf(inEnglish, one).trim()).not.toBe('');
    expect(sampleOf(inSpanish, one).trim()).not.toBe('');
    expect(sampleOf(inSpanish, one)).not.toMatch(/undefined|\[object/);
  }
}
