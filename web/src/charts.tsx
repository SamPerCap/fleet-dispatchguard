import { useId } from 'react';
import { areaPath, gapSegments, linePath, ringDash, scalePoints, scaleSeries } from './chartmath';

/** Literal classes, so that Tailwind keeps them. */
const STROKE = {
  blue: 'text-blue',
  success: 'text-success',
  warning: 'text-warning',
  critical: 'text-critical',
  bright: 'text-blue-bright',
  'success-bright': 'text-success-bright',
} as const;
export type ChartTone = keyof typeof STROKE;

export function Sparkline({
  values,
  tone = 'blue',
  label,
  width = 96,
  height = 32,
}: {
  values: number[];
  tone?: ChartTone;
  label: string;
  width?: number;
  height?: number;
}) {
  const id = useId();
  const pts = scalePoints(values, width, height, 3);
  if (pts.length === 0) return null;
  const last = pts[pts.length - 1];
  return (
    <svg
      role="img"
      aria-label={label}
      viewBox={`0 0 ${width} ${height}`}
      width={width}
      height={height}
      className={`shrink-0 overflow-visible ${STROKE[tone]}`}
    >
      <defs>
        <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="currentColor" stopOpacity="0.22" />
          <stop offset="100%" stopColor="currentColor" stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d={areaPath(pts, height)} fill={`url(#${id})`} />
      <path
        d={linePath(pts)}
        fill="none"
        stroke="currentColor"
        strokeWidth={2}
        strokeLinecap="round"
        strokeLinejoin="round"
        vectorEffect="non-scaling-stroke"
      />
      <circle cx={last.x} cy={last.y} r={2.5} fill="currentColor" />
    </svg>
  );
}

export function Ring({
  percent,
  size = 64,
  stroke = 7,
  tone = 'success',
  label,
  onNavy,
  children,
}: {
  percent: number;
  size?: number;
  stroke?: number;
  tone?: ChartTone;
  label: string;
  onNavy?: boolean;
  children?: React.ReactNode;
}) {
  const radius = (size - stroke) / 2;
  const { circumference, dash } = ringDash(percent, radius);
  return (
    <div className="relative grid shrink-0 place-items-center" style={{ width: size, height: size }}>
      <svg
        role="img"
        aria-label={label}
        viewBox={`0 0 ${size} ${size}`}
        width={size}
        height={size}
        className={`-rotate-90 ${STROKE[tone]}`}
      >
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          strokeWidth={stroke}
          className={onNavy ? 'stroke-white/15' : 'stroke-muted'}
        />
        {dash > 0 && (
          <circle
            cx={size / 2}
            cy={size / 2}
            r={radius}
            fill="none"
            stroke="currentColor"
            strokeWidth={stroke}
            strokeLinecap="round"
            strokeDasharray={`${dash} ${circumference}`}
          />
        )}
      </svg>
      {children && (
        <div className="absolute inset-0 grid place-items-center text-center" aria-hidden>
          {children}
        </div>
      )}
    </div>
  );
}

/** `upTo` cuts the line at the last real value: hours still to come are not drawn as 0. */
export function AreaChart({
  values,
  label,
  marks,
  upTo,
  tone = 'bright',
  height = 96,
  onNavy,
}: {
  values: number[];
  label: string;
  marks: { index: number; text: string }[];
  upTo?: number;
  tone?: ChartTone;
  height?: number;
  onNavy?: boolean;
}) {
  const id = useId();
  const width = 300;
  const count = values.length;
  const all = scalePoints(values.length ? values : [0], width, height, 6);
  const shown = upTo == null ? all : all.slice(0, Math.max(1, Math.min(upTo + 1, all.length)));
  const toX = (index: number) => (count > 1 ? (index / (count - 1)) * 100 : 0);
  return (
    <div>
      <svg
        role="img"
        aria-label={label}
        viewBox={`0 0 ${width} ${height}`}
        preserveAspectRatio="none"
        width="100%"
        height={height}
        className={`block overflow-visible ${STROKE[tone]}`}
      >
        <defs>
          <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="currentColor" stopOpacity="0.35" />
            <stop offset="100%" stopColor="currentColor" stopOpacity="0" />
          </linearGradient>
        </defs>
        {[0.25, 0.5, 0.75].map(fraction => (
          <line
            key={fraction}
            x1={0}
            x2={width}
            y1={height * fraction}
            y2={height * fraction}
            vectorEffect="non-scaling-stroke"
            className={onNavy ? 'stroke-white/10' : 'stroke-line'}
            strokeWidth={1}
          />
        ))}
        <path d={areaPath(shown, height)} fill={`url(#${id})`} />
        <path
          d={linePath(shown)}
          fill="none"
          stroke="currentColor"
          strokeWidth={2.5}
          strokeLinejoin="round"
          strokeLinecap="round"
          vectorEffect="non-scaling-stroke"
        />
      </svg>
      <div className={`relative mt-1 h-4 text-xs ${onNavy ? 'text-on-navy-2' : 'text-ink-3'}`} aria-hidden>
        {marks.map((mark, index) => (
          <span
            key={mark.index}
            className={`absolute ${index === 0 ? '' : index === marks.length - 1 ? '-translate-x-full' : '-translate-x-1/2'}`}
            style={{ left: `${toX(mark.index)}%` }}
          >
            {mark.text}
          </span>
        ))}
      </div>
    </div>
  );
}

export function MiniBars({
  values,
  label,
  highlight,
  height = 56,
  onNavy,
}: {
  values: number[];
  label: string;
  highlight?: number;
  height?: number;
  onNavy?: boolean;
}) {
  const max = Math.max(1, ...values);
  return (
    <div role="img" aria-label={label} className="flex items-end gap-[3px]" style={{ height }}>
      {values.map((value, index) => (
        <span
          key={index}
          aria-hidden
          className={`min-w-0 flex-1 rounded-t-[3px] ${
            index === highlight ? (onNavy ? 'bg-blue-bright' : 'bg-blue') : onNavy ? 'bg-blue-bright/35' : 'bg-blue/30'
          }`}
          style={{ height: `${Math.max(value > 0 ? 8 : 3, (value / max) * 100)}%` }}
        />
      ))}
    </div>
  );
}

/** Every value has a dot so that an isolated hour is visible. A gap (`null`) is an hour without attempts, never a 0. */
export function GapLines({
  series,
  label,
  marks,
  height = 160,
}: {
  series: { name: string; tone: ChartTone; values: (number | null)[] }[];
  label: string;
  marks: { index: number; text: string }[];
  height?: number;
}) {
  const width = 600;
  const count = Math.max(0, ...series.map(s => s.values.length));
  const scaled = scaleSeries(
    series.map(s => s.values),
    width,
    height,
    8,
  );
  const toX = (index: number) => (count > 1 ? (index / (count - 1)) * 100 : 50);
  return (
    <div>
      <svg
        role="img"
        aria-label={label}
        viewBox={`0 0 ${width} ${height}`}
        preserveAspectRatio="none"
        width="100%"
        height={height}
        className="block overflow-visible"
      >
        {[0.25, 0.5, 0.75].map(fraction => (
          <line
            key={fraction}
            x1={0}
            x2={width}
            y1={height * fraction}
            y2={height * fraction}
            vectorEffect="non-scaling-stroke"
            className="stroke-line"
            strokeWidth={1}
          />
        ))}
        <line x1={0} x2={width} y1={height - 8} y2={height - 8} vectorEffect="non-scaling-stroke" className="stroke-line" strokeWidth={1} />
        {series.map((line, lineIndex) => (
          <g key={line.name} className={STROKE[line.tone]}>
            {gapSegments(scaled[lineIndex]).map((seg, segmentIndex) => (
              <path
                key={segmentIndex}
                d={linePath(seg)}
                fill="none"
                stroke="currentColor"
                strokeWidth={2}
                strokeLinejoin="round"
                strokeLinecap="round"
                vectorEffect="non-scaling-stroke"
              />
            ))}
            {/* A zero-length stroke with a round cap stays round even when the SVG is stretched. */}
            {scaled[lineIndex].map(
              (point, index) =>
                point && (
                  <path
                    key={index}
                    d={`M${point.x} ${point.y} L${point.x} ${point.y}`}
                    stroke="currentColor"
                    strokeWidth={6}
                    strokeLinecap="round"
                    vectorEffect="non-scaling-stroke"
                  />
                ),
            )}
          </g>
        ))}
      </svg>
      <div className="relative mt-1 h-4 text-xs text-ink-3" aria-hidden>
        {marks.map((mark, index) => (
          <span
            key={mark.index}
            className={`absolute whitespace-nowrap ${index === 0 && toX(mark.index) < 3 ? '' : toX(mark.index) > 97 ? '-translate-x-full' : '-translate-x-1/2'}`}
            style={{ left: `${toX(mark.index)}%` }}
          >
            {mark.text}
          </span>
        ))}
      </div>
    </div>
  );
}

const BAR_TONE = { normal: 'bg-blue', extended: 'bg-warning', over: 'bg-critical' } as const;
const LINE_TONE = { warning: 'border-warning text-warning-ink', critical: 'border-critical text-critical-ink' } as const;

/** A day at 0 keeps a minimal stroke so that it is visible. `tones` always comes with text in `titles`: never colour alone. */
export function DayBars({
  values,
  label,
  ceiling,
  marks,
  titles,
  tones,
  lines = [],
  height = 168,
}: {
  values: number[];
  label: string;
  ceiling: number;
  marks: { index: number; text: string }[];
  titles: string[];
  tones?: (keyof typeof BAR_TONE)[];
  lines?: { value: number; text: string; tone: keyof typeof LINE_TONE }[];
  height?: number;
}) {
  const count = values.length;
  const pct = (value: number) => `${Math.min(100, Math.max(0, (value / ceiling) * 100))}%`;
  const lowest = lines.length > 1 ? Math.min(...lines.map(l => l.value)) : null;
  return (
    <div>
      <div role="img" aria-label={label} className="relative" style={{ height }}>
        <div className="absolute inset-0 border-b border-line" aria-hidden />
        <div className="absolute inset-0 flex items-end gap-[3px]" aria-hidden>
          {values.map((value, index) => (
            <span key={index} title={titles[index]} className="flex h-full min-w-0 flex-1 items-end">
              <span
                className={`w-full rounded-t-[3px] ${value > 0 ? BAR_TONE[tones?.[index] ?? 'normal'] : 'bg-muted'}`}
                style={{ height: value > 0 ? `max(3px, ${pct(value)})` : '2px' }}
              />
            </span>
          ))}
        </div>
        {lines.map(line => (
          <div
            key={line.text}
            aria-hidden
            className={`pointer-events-none absolute inset-x-0 z-10 border-t border-dashed ${LINE_TONE[line.tone]}`}
            style={{ bottom: pct(line.value) }}
          >
            {/* Above the bars. The lowest line has its label below it: two close limits (9 h and 10 h) do not overlap. */}
            <span
              className={`absolute right-0 rounded bg-surface/90 px-1 text-xs font-medium leading-4 ${line.value === lowest ? 'top-[2px]' : '-top-[18px]'}`}
            >
              {line.text}
            </span>
          </div>
        ))}
      </div>
      <div className="relative mt-1 h-4 text-xs text-ink-3" aria-hidden>
        {marks.map(mark => {
          const x = count > 0 ? ((mark.index + 0.5) / count) * 100 : 0;
          return (
            <span
              key={mark.index}
              className={`absolute whitespace-nowrap ${x > 94 ? '-translate-x-full' : x < 4 ? '' : '-translate-x-1/2'}`}
              style={{ left: x > 94 ? '100%' : x < 4 ? '0' : `${x}%` }}
            >
              {mark.text}
            </span>
          );
        })}
      </div>
    </div>
  );
}
