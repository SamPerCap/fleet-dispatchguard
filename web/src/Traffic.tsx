/** The fleet going by while the visitor waits. Pure decoration: hidden from screen readers and when motion is reduced. */
const CONVOY = [
  { Vehicle: Coach, tone: 'text-blue' },
  { Vehicle: Minibus, tone: 'text-warning' },
  { Vehicle: CityBus, tone: 'text-critical' },
  { Vehicle: DoubleDecker, tone: 'text-success' },
  { Vehicle: Minibus, tone: 'text-blue-bright' },
];

/** All at the same speed, so that none runs over another. */
const CROSSING_SECONDS = 15;

export function Traffic() {
  return (
    <div className="traffic" aria-hidden>
      {CONVOY.map(({ Vehicle, tone }, place) => (
        <span
          key={place}
          className={`traffic-vehicle ${tone}`}
          // A negative delay starts it halfway: the road is never empty.
          style={{ animationDuration: `${CROSSING_SECONDS}s`, animationDelay: `${(-CROSSING_SECONDS * (place + 0.5)) / CONVOY.length}s` }}
        >
          <span className="traffic-bounce" style={{ animationDelay: `${-0.17 * place}s` }}>
            <Vehicle />
          </span>
        </span>
      ))}
    </div>
  );
}

function Wheel({ x, y }: { x: number; y: number }) {
  return (
    <g className="traffic-wheel" style={{ transformOrigin: `${x}px ${y}px` }}>
      <circle cx={x} cy={y} r="6" className="fill-ink" />
      <circle cx={x} cy={y} r="2.5" className="fill-surface" />
      <path d={`M${x - 5} ${y}h10M${x} ${y - 5}v10`} className="stroke-surface" strokeWidth="1" />
    </g>
  );
}

function Windows({ from, count, y, width = 11, height = 11, gap = 4 }: { from: number; count: number; y: number; width?: number; height?: number; gap?: number }) {
  return Array.from({ length: count }, (_, index) => (
    <rect key={index} x={from + index * (width + gap)} y={y} width={width} height={height} rx="2" className="fill-surface" />
  ));
}

function Headlight({ x, y }: { x: number; y: number }) {
  return <rect x={x} y={y} width="3" height="5" rx="1" className="fill-warning-bright" />;
}

function CityBus() {
  return (
    <svg width="96" height="48" viewBox="0 0 96 48">
      <rect x="2" y="8" width="90" height="32" rx="6" fill="currentColor" />
      <Windows from={8} count={4} y={14} />
      <rect x="70" y="14" width="18" height="15" rx="3" className="fill-surface" />
      <Headlight x={89} y={32} />
      <Wheel x={22} y={40} />
      <Wheel x={74} y={40} />
    </svg>
  );
}

function Coach() {
  return (
    <svg width="112" height="48" viewBox="0 0 112 48">
      <path d="M8 6h84c8 0 14 10 16 22v8a4 4 0 0 1-4 4H8a6 6 0 0 1-6-6V12a6 6 0 0 1 6-6z" fill="currentColor" />
      <Windows from={9} count={5} y={12} width={12} />
      <path d="M90 12h3c5 0 9 6 11 14H90z" className="fill-surface" />
      <Headlight x={104} y={31} />
      <Wheel x={24} y={40} />
      <Wheel x={88} y={40} />
    </svg>
  );
}

function Minibus() {
  return (
    <svg width="68" height="48" viewBox="0 0 68 48">
      <path d="M8 16h36c8 0 14 6 18 13l3 3v5a3 3 0 0 1-3 3H8a6 6 0 0 1-6-6V22a6 6 0 0 1 6-6z" fill="currentColor" />
      <Windows from={8} count={2} y={21} width={12} height={9} />
      <path d="M42 21h3c5 0 8 4 11 9H42z" className="fill-surface" />
      <Headlight x={61} y={32} />
      <Wheel x={18} y={40} />
      <Wheel x={50} y={40} />
    </svg>
  );
}

function DoubleDecker() {
  return (
    <svg width="92" height="48" viewBox="0 0 92 48">
      <rect x="2" y="1" width="86" height="39" rx="5" fill="currentColor" />
      <Windows from={8} count={5} y={5} height={9} />
      <Windows from={8} count={4} y={20} height={9} />
      <rect x="69" y="20" width="15" height="13" rx="2" className="fill-surface" />
      <Headlight x={85} y={34} />
      <Wheel x={22} y={40} />
      <Wheel x={70} y={40} />
    </svg>
  );
}
