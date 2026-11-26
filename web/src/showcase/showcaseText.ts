import { language, withTexts, type Language } from '../language';
import { en } from './text.en';
import type { ShowcaseText } from './text';

// English comes with the page, which is what whoever has chosen nothing reads; Spanish is asked for only when it is chosen.
let spanish: ShowcaseText | null = null;

async function load(next: Language): Promise<void> {
  if (next === 'es') spanish ??= (await import('./text.es')).es;
}

export const showcaseText = (): ShowcaseText => (language() === 'es' && spanish ? spanish : en);

export const showcaseReady = (): Promise<void> => withTexts(load);
