import { withTexts, type Language } from '../language';
import type { Texts } from './en';

/** The texts of the application in the chosen language. They are read when they are shown, never kept: the language may change. */
export let t: Texts;

/** One file for each language, and only the one that is read is asked for. */
const CATALOGUES: Record<Language, () => Promise<Texts>> = {
  en: () => import('./en').then(module => module.en),
  es: () => import('./es').then(module => module.es),
};

export async function loadCatalogue(language: Language): Promise<void> {
  t = await CATALOGUES[language]();
}

let ready: Promise<void> | null = null;
export const catalogueReady = (): Promise<void> => (ready ??= withTexts(loadCatalogue));
