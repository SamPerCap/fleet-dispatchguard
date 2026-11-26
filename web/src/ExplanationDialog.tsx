import { t } from './i18n/t';
import * as Dialog from '@radix-ui/react-dialog';
import * as Tabs from '@radix-ui/react-tabs';
import { Check, Info, Minus, TriangleAlert, X } from 'lucide-react';
import { adrName, outcomeLabel } from './explanation';
import type { Explanation, ExplanationOutcome } from './types';

const ICON: Record<ExplanationOutcome, JSX.Element> = {
  Passed: <Check className="h-4 w-4 text-success-ink" aria-hidden />,
  Warning: <TriangleAlert className="h-4 w-4 text-warning-ink" aria-hidden />,
  Failed: <X className="h-4 w-4 text-critical-ink" aria-hidden />,
  NotEvaluated: <Minus className="h-4 w-4 text-ink-2" aria-hidden />,
};

function Outcome({ outcome }: { outcome: string }) {
  const label = outcomeLabel(outcome);
  return (
    <span className="mt-0.5 shrink-0" title={label.text}>
      {ICON[outcome as ExplanationOutcome] ?? label.icon}
      <span className="sr-only">{label.text}</span>
    </span>
  );
}

export function InfoButton({ explanation, label }: { explanation?: Explanation | null; label: string }) {
  if (!explanation) return null;
  return (
    <Dialog.Root>
      <Dialog.Trigger asChild>
        <button
          type="button"
          className="relative inline-flex rounded-full p-0.5 text-blue-ink hover:bg-blue-soft after:absolute after:left-1/2 after:top-1/2 after:h-8 after:w-8 after:-translate-x-1/2 after:-translate-y-1/2 after:content-['']"
          aria-label={t.explanation.of(label)}
          title={t.explanation.whatHappened}
          onClick={e => e.stopPropagation()}
        >
          <Info className="h-4 w-4" aria-hidden />
        </button>
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-navy/40" />
        <Dialog.Content
          aria-describedby={undefined}
          className="fixed left-1/2 top-1/2 z-50 max-h-[calc(100vh-48px)] w-[min(720px,calc(100vw-32px))] -translate-x-1/2 -translate-y-1/2 overflow-auto rounded-lg bg-surface p-6 shadow-pop"
        >
          <div className="flex items-start justify-between gap-3">
            <Dialog.Title className="text-module font-semibold text-ink">{label}</Dialog.Title>
            <Dialog.Close className="btn btn-text btn-icon" aria-label={t.common.close}>
              <X className="h-4 w-4" aria-hidden />
            </Dialog.Close>
          </div>
          <ExplanationBody explanation={explanation} />
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

export function ExplanationBody({ explanation }: { explanation: Explanation }) {
  return (
    <>
      <p className="mb-3 mt-2">{explanation.Summary}</p>
      <Tabs.Root defaultValue="business">
        <Tabs.List className="seg mb-3" aria-label={t.explanation.layer}>
          <Tabs.Trigger value="business">{t.explanation.business}</Tabs.Trigger>
          <Tabs.Trigger value="tech">{t.explanation.developers}</Tabs.Trigger>
        </Tabs.List>
        <Tabs.Content value="business">
          <ul className="space-y-2">
            {explanation.Business.map((item, index) => (
              <li key={index} className="flex gap-3 rounded-md bg-sunken px-3 py-2.5">
                <Outcome outcome={item.Outcome} />
                <div>
                  <strong className="font-semibold text-ink">{item.Title}</strong>
                  {item.Reference && <span className="chip ml-2">{item.Reference}</span>}
                  <p className="text-ink-2">{item.Detail}</p>
                </div>
              </li>
            ))}
          </ul>
        </Tabs.Content>
        <Tabs.Content value="tech" className="space-y-3">
          <h4 className="font-medium">{t.explanation.flow}</h4>
          <ol className="space-y-2">
            {explanation.Flow.map((step, index) => (
              <li key={index} className="flex gap-2">
                <Outcome outcome={step.Outcome} />
                <div>
                  <code className="text-xs">{step.Component}</code>
                  <p>{step.Role}</p>
                  <p className="text-ink-2">→ {step.Result}</p>
                </div>
              </li>
            ))}
          </ol>
          <h4 className="font-medium">{t.explanation.patterns}</h4>
          <ul className="list-disc pl-5">
            {explanation.Patterns.map(pattern => (
              <li key={pattern}>{pattern}</li>
            ))}
          </ul>
          <h4 className="font-medium">{t.explanation.adrs}</h4>
          <ul className="list-disc pl-5">
            {explanation.Adrs.map(adr => (
              <li key={adr}>
                <strong>{adrName(adr)}</strong> <code className="text-xs">{adr}</code>
              </li>
            ))}
          </ul>
        </Tabs.Content>
      </Tabs.Root>
    </>
  );
}
