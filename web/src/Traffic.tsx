import { useState, type AnimationEvent } from 'react';

const QUEUE = [
  { Vehicle: Coach, tone: 'text-blue', width: 112 },
  { Vehicle: Minibus, tone: 'text-warning', width: 68 },
  { Vehicle: CityBus, tone: 'text-critical', width: 96 },
  { Vehicle: DoubleDecker, tone: 'text-success', width: 92 },
  { Vehicle: Minibus, tone: 'text-blue-bright', width: 68 },
];
const QUEUE_GAP = 12;
/** Where the stop line is, measured from the right end of the road; the light stands just after it. */
const STOP_LINE = 'max(56px, 8%)';
/** One after another from the left, so the queue grows while the wait lasts. */
const ARRIVE_EVERY_MS = 1_300;
/** The first in the queue leaves first, the rest follow as it opens up. */
const LEAVE_STAGGER_MS = 120;

type Progress = 'waiting' | 'arriving' | 'stopped';

/**
 * Vehicles come in from the left and stop at a red light on the right, queueing up while the application loads. On green, those
 * in the queue drive off, one still braking finishes and follows, and one that had not come in yet never appears: nothing starts
 * again from the left (SPEC 0101). Decorative.
 */
export function TrafficRoad({ green }: { green: boolean }) {
  const [progress, setProgress] = useState<Progress[]>(() => QUEUE.map(() => 'waiting'));
  const mark = (place: number, next: Progress) => (event: AnimationEvent) => {
    // The bounce and the wheels inside animate too: only the vehicle's own drive counts.
    if (event.target !== event.currentTarget) return;
    setProgress(current => current.map((value, index) => (index === place ? next : value)));
  };
  let fromStopLine = 0;
  return (
    <div className="traffic-road" aria-hidden>
      {QUEUE.map(({ Vehicle, tone, width }, place) => {
        const right = fromStopLine;
        fromStopLine += width + QUEUE_GAP;
        const state = progress[place];
        if (green && state === 'waiting') return null;
        const leaving = green && state === 'stopped';
        return (
          <span
            key={`${place}-${leaving ? 'leave' : 'arrive'}`}
            className={`traffic-car ${leaving ? 'is-leaving' : 'is-arriving'} ${state === 'stopped' && !green ? 'is-stopped' : ''} ${tone}`}
            style={{ right: `calc(${STOP_LINE} + ${right}px)`, animationDelay: `${leaving ? place * LEAVE_STAGGER_MS : place * ARRIVE_EVERY_MS}ms` }}
            onAnimationStart={leaving ? undefined : mark(place, 'arriving')}
            onAnimationEnd={leaving ? undefined : mark(place, 'stopped')}
          >
            <span className="traffic-idle" style={{ animationDelay: `${-0.13 * place}s` }}>
              <Vehicle />
            </span>
          </span>
        );
      })}
      <span className="traffic-light-post" style={{ right: `calc(${STOP_LINE} - 44px)` }}>
        <TrafficLight green={green} />
      </span>
    </div>
  );
}

function TrafficLight({ green }: { green: boolean }) {
  return (
    <svg width="34" height="84" viewBox="0 0 34 84" className="block">
      <rect x="1" y="1" width="32" height="82" rx="8" className="fill-ink" />
      <circle cx="17" cy="16" r="9" className={green ? 'fill-ink-3 opacity-40' : 'fill-critical-bright traffic-lamp'} />
      <circle cx="17" cy="42" r="9" className="fill-ink-3 opacity-40" />
      <circle cx="17" cy="68" r="9" className={green ? 'fill-success-bright traffic-lamp' : 'fill-ink-3 opacity-40'} />
    </svg>
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
