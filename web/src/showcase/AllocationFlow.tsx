import { useId } from 'react';
import { showcaseText } from './showcaseText';
import type { FlowStepId } from './text';

/**
 * The order of `AllocationEvaluator.DecideAsync`, and the one step a person can override (SPEC 0056). If the engine changes its
 * order, this changes with it: `AllocationFlow.test.ts` keeps the two lists side by side.
 */
export const FLOW_STEPS: { id: FlowStepId; forcible: boolean }[] = [
  { id: 'driver', forcible: false },
  { id: 'compliance', forcible: true },
  { id: 'vehicle', forcible: false },
  { id: 'overlap', forcible: false },
];

// Drawn for a phone first (360 px wide): the boxes keep a readable size when the drawing is scaled down.
const WIDTH = 360;
const BOX = 250;
const OUTCOME_X = 274;
const GAP = 26;
const STEP = 62;
const FORCIBLE_STEP = 84;

interface Row {
  key: string;
  top: number;
  height: number;
}

function layout(): { rows: Row[]; height: number } {
  const rows: Row[] = [{ key: 'request', top: 0, height: 56 }];
  let top = 56 + GAP;
  for (const step of FLOW_STEPS) {
    const height = step.forcible ? FORCIBLE_STEP : STEP;
    rows.push({ key: step.id, top, height });
    top += height + GAP;
  }
  rows.push({ key: 'saved', top, height: 56 });
  return { rows, height: top + 56 };
}

/** Accessible SVG: the drawing is one image whose name and description say the whole flow (SPEC 0096 R3). */
export function AllocationFlow() {
  const text = showcaseText().flow;
  // React's ids carry colons, which a url(#…) reference does not accept.
  const base = useId().replace(/:/g, '');
  const titleId = `${base}-title`;
  const descriptionId = `${base}-description`;
  const { rows, height } = layout();
  const lines = (key: string) =>
    key === 'request' ? text.request : key === 'saved' ? text.saved : text.steps[key as FlowStepId];

  return (
    <figure className="module">
      <svg
        viewBox={`0 0 ${WIDTH} ${height}`}
        role="img"
        aria-labelledby={`${titleId} ${descriptionId}`}
        className="mx-auto block h-auto w-full max-w-[520px]"
      >
        <title id={titleId}>{text.title}</title>
        <desc id={descriptionId}>{text.description}</desc>
        <defs>
          <marker id={`${titleId}-arrow`} viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
            <path d="M0 0 L10 5 L0 10 z" className="fill-[rgb(var(--ink-3))]" />
          </marker>
        </defs>
        {rows.map((row, index) => {
          const step = FLOW_STEPS.find(s => s.id === row.key);
          const isEnd = row.key === 'saved';
          const middle = row.top + row.height / 2;
          const next = rows[index + 1];
          return (
            <g key={row.key}>
              <rect
                x={0}
                y={row.top}
                width={BOX}
                height={row.height}
                rx={10}
                className={isEnd ? 'fill-[rgb(var(--success-soft))]' : step ? 'fill-[rgb(var(--blue-soft))]' : 'fill-[rgb(var(--navy-soft))]'}
              />
              <text x={14} y={row.top + 24} className="fill-[rgb(var(--ink))] text-[15px] font-bold">
                {lines(row.key).name}
              </text>
              <text x={14} y={row.top + 45} className="fill-[rgb(var(--ink-2))] text-[13px]">
                {lines(row.key).detail}
              </text>
              {step?.forcible && (
                <>
                  <rect x={10} y={row.top + 56} width={BOX - 20} height={20} rx={6} className="fill-[rgb(var(--warning-soft))] stroke-[rgb(var(--warning))]" strokeDasharray="4 3" />
                  <text x={18} y={row.top + 70} className="fill-[rgb(var(--warning-ink))] text-[12px] font-semibold">
                    {text.forcible}
                  </text>
                </>
              )}
              {step && (
                <>
                  <line x1={BOX} y1={middle} x2={OUTCOME_X - 2} y2={middle} className="stroke-[rgb(var(--ink-3))]" strokeWidth={1.5} markerEnd={`url(#${titleId}-arrow)`} />
                  <rect x={OUTCOME_X} y={middle - 20} width={WIDTH - OUTCOME_X} height={40} rx={8} className="fill-[rgb(var(--critical-soft))]" />
                  <text x={OUTCOME_X + 10} y={middle - 3} className="fill-[rgb(var(--critical-ink))] text-[12px] font-semibold">
                    {text.rejected[0]}
                  </text>
                  <text x={OUTCOME_X + 10} y={middle + 13} className="fill-[rgb(var(--critical-ink))] text-[12px] font-semibold">
                    {text.rejected[1]}
                  </text>
                </>
              )}
              {next && (
                <line
                  x1={BOX / 2}
                  y1={row.top + row.height}
                  x2={BOX / 2}
                  y2={next.top - 2}
                  className="stroke-[rgb(var(--ink-3))]"
                  strokeWidth={1.5}
                  markerEnd={`url(#${titleId}-arrow)`}
                />
              )}
            </g>
          );
        })}
      </svg>
      <figcaption className="mt-4 text-data text-ink-2">{text.caption}</figcaption>
    </figure>
  );
}
