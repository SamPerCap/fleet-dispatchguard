import { t } from './i18n/t';
import type { ExplanationOutcome } from './types';

const LABELS: Record<ExplanationOutcome, { icon: string; text: string }> = {
  Passed: { icon: '✓', get text() { return t.explanation.passed; } },
  Warning: { icon: '⚠', get text() { return t.explanation.warning; } },
  Failed: { icon: '✗', get text() { return t.explanation.failed; } },
  NotEvaluated: { icon: '–', get text() { return t.explanation.notEvaluated; } },
};

/** An unknown value is shown as it comes instead of breaking. */
export function outcomeLabel(outcome: string): { icon: string; text: string } {
  return LABELS[outcome as ExplanationOutcome] ?? { icon: '?', text: outcome };
}

export function adrName(path: string): string {
  const match = /(\d{4})-[^/]*\.md$/.exec(path);
  return match ? `ADR ${match[1]}` : path;
}
