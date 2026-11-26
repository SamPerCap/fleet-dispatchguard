import { t } from './i18n/t';

/** Of the API (`AllocationAttempt.Anonymous`): what is stored for an attempt without a name. */
const ANONYMOUS = '(sin nombre)';

export const whoText = (name: string | null | undefined): string => {
  const declared = name?.trim();
  return !declared || declared === ANONYMOUS ? t.common.noNameLower : declared;
};

/** Skips particles ("de", "del", "la", "y"). */
export function initials(name: string | null | undefined): string {
  const words = (name ?? '')
    .trim()
    .split(/\s+/)
    .filter(w => w && !/^(de|del|la|las|los|y)$/i.test(w));
  if (words.length === 0) return '?';
  const first = [...words[0]][0] ?? '';
  const second = words.length > 1 ? ([...words[1]][0] ?? '') : '';
  return (first + second).toLocaleUpperCase('es');
}

/** Always the same tone for the same person, from a palette with AA text. */
const TONES = ['blue', 'success', 'warning', 'navy'] as const;
export type AvatarTone = (typeof TONES)[number];
export function avatarTone(key: string | number): AvatarTone {
  const text = String(key);
  let hash = 0;
  for (const char of text) hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
  return TONES[hash % TONES.length];
}
