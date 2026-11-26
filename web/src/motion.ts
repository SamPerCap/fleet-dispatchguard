export const NUMBER_TWEEN_MS = 400;

export const easeOutCubic = (progress: number) => 1 - (1 - progress) ** 3;

/** Rounded to `decimals` so that it never shows decimals the figure does not have; at progress ≥ 1 it returns exactly `to`. */
export function tween(from: number, to: number, progress: number, decimals = 0): number {
  const clamped = Math.min(1, Math.max(0, progress));
  if (clamped >= 1) return to;
  const factor = 10 ** decimals;
  return Math.round((from + (to - from) * easeOutCubic(clamped)) * factor) / factor;
}

export function decimalsOf(value: number): number {
  if (!Number.isFinite(value) || Number.isInteger(value)) return 0;
  const text = String(value);
  const dotIndex = text.indexOf('.');
  return dotIndex < 0 ? 0 : Math.min(3, text.length - dotIndex - 1);
}

/** Without `matchMedia`, assume yes: nothing is animated. */
export function prefersReducedMotion(): boolean {
  try {
    return typeof matchMedia !== 'function' || matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch {
    return true;
  }
}
