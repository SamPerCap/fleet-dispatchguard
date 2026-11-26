import { chooseLanguage, type Language } from './language';
import { useLanguage } from './useLanguage';

/** Each language in its own words: whoever needs the switch may not read the other one. */
const LANGUAGES: { id: Language; code: string; name: string }[] = [
  { id: 'en', code: 'EN', name: 'English' },
  // eslint-disable-next-line no-restricted-syntax -- each language is named in its own words, whatever the one shown.
  { id: 'es', code: 'ES', name: 'Español' },
];
const LABEL = 'Language / Idioma';

const TONES = {
  light: { chosen: 'bg-surface text-blue-ink shadow-1', other: 'text-ink-2 hover:text-ink' },
  navy: { chosen: 'bg-white/15 text-white', other: 'text-side-fg hover:text-white' },
};

export function LanguageSwitch({ tone = 'light', className = '' }: { tone?: keyof typeof TONES; className?: string }) {
  const chosen = useLanguage();
  return (
    <div role="group" aria-label={LABEL} className={`flex gap-1 ${className}`}>
      {LANGUAGES.map(({ id, code, name }) => (
        <button
          key={id}
          type="button"
          lang={id}
          aria-label={name}
          aria-pressed={id === chosen}
          onClick={() => void chooseLanguage(id)}
          className={`flex h-11 w-11 items-center justify-center rounded-md text-data font-semibold ${TONES[tone][id === chosen ? 'chosen' : 'other']}`}
        >
          {code}
        </button>
      ))}
    </div>
  );
}
