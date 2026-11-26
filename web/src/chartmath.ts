import { t } from './i18n/t';
export interface Point {
  x: number;
  y: number;
}

/**
 * The minimum is always 0 (the product figures are counts) unless `fromMin` is set. A single value draws a flat line from side to
 * side.
 */
export function scalePoints(values: number[], width: number, height: number, pad = 2, fromMin = false): Point[] {
  if (values.length === 0) return [];
  const finite = values.map(v => (Number.isFinite(v) ? v : 0));
  const max = Math.max(...finite);
  const min = fromMin ? Math.min(...finite) : Math.min(0, ...finite);
  const span = max - min;
  const usable = height - pad * 2;
  // Without a range it goes at half height; all zeros go on the baseline (a curve without data must not look like activity).
  const toY = (value: number) => (span === 0 ? (max === 0 ? pad + usable : height / 2) : pad + usable - ((value - min) / span) * usable);
  if (finite.length === 1)
    return [
      { x: 0, y: toY(finite[0]) },
      { x: width, y: toY(finite[0]) },
    ];
  const step = width / (finite.length - 1);
  return finite.map((value, index) => ({ x: round(index * step), y: round(toY(value)) }));
}

const round = (value: number) => Math.round(value * 100) / 100;

export function linePath(points: Point[]): string {
  return points.map((point, index) => `${index === 0 ? 'M' : 'L'}${point.x} ${point.y}`).join(' ');
}

export function areaPath(points: Point[], height: number): string {
  if (points.length === 0) return '';
  const first = points[0],
    last = points[points.length - 1];
  return `${linePath(points)} L${last.x} ${height} L${first.x} ${height} Z`;
}

export function ringDash(percent: number, radius: number): { circumference: number; dash: number } {
  const circumference = 2 * Math.PI * radius;
  const clamped = Number.isFinite(percent) ? Math.min(100, Math.max(0, percent)) : 0;
  return { circumference: round(circumference), dash: round((clamped / 100) * circumference) };
}

export function seriesText(label: string, values: number[], format: (value: number) => string = String): string {
  if (values.length === 0) return t.charts.noData(label);
  const parts = values.map(format);
  const list = parts.length === 1 ? parts[0] : `${parts.slice(0, -1).join(', ')} y ${parts[parts.length - 1]}`;
  return `${label}: ${list}`;
}

/** -1 if there are no values. */
export function peakIndex(values: number[]): number {
  if (values.length === 0) return -1;
  let best = 0;
  values.forEach((value, index) => {
    if (value > values[best]) best = index;
  });
  return best;
}

/** `null` is "no data", not 0. All series share the same scale. */
export function scaleSeries(series: (number | null)[][], width: number, height: number, pad = 2): (Point | null)[][] {
  const length = Math.max(0, ...series.map(s => s.length));
  const finite = series.flat().filter((v): v is number => v != null && Number.isFinite(v));
  const max = Math.max(0, ...finite);
  const usable = height - pad * 2;
  const step = length > 1 ? width / (length - 1) : 0;
  const toY = (value: number) => (max === 0 ? pad + usable : pad + usable - (Math.max(0, value) / max) * usable);
  return series.map(values =>
    values.map((value, index) =>
      value == null || !Number.isFinite(value) ? null : { x: round(length > 1 ? index * step : width / 2), y: round(toY(value)) },
    ),
  );
}

export function gapSegments<T>(points: (T | null)[]): T[][] {
  const out: T[][] = [];
  let run: T[] = [];
  for (const point of points) {
    if (point == null) {
      if (run.length) out.push(run);
      run = [];
    } else run.push(point);
  }
  if (run.length) out.push(run);
  return out;
}
