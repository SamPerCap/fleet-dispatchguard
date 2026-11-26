// Own artwork, drawn by hand for this repo (no third-party images). Decorative (aria-hidden): the product name is written next to
// it.

export function BrandMark({ size = 36 }: { size?: number }) {
  return (
    <svg viewBox="0 0 40 40" width={size} height={size} aria-hidden className="shrink-0">
      <defs>
        <linearGradient id="brand-mark" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="rgb(var(--blue-bright))" />
          <stop offset="100%" stopColor="rgb(var(--blue))" />
        </linearGradient>
      </defs>
      <rect x="2" y="2" width="36" height="36" rx="11" fill="url(#brand-mark)" />
      <path d="M11 28 C 17 28, 15 18, 21 18 S 26 12, 29 12" fill="none" stroke="white" strokeWidth="3.2" strokeLinecap="round" />
      <circle cx="11" cy="28" r="2.6" fill="white" fillOpacity="0.55" />
      <circle cx="29" cy="12" r="3.4" fill="white" />
    </svg>
  );
}

/** Drawn on the right and fading to the left (the caller sets the mask), so that the header text keeps its contrast. */
export function BrandScene({ className = '' }: { className?: string }) {
  // Placed by hand (x, width, height) so that no pattern repeats.
  const far: [number, number, number][] = [
    [0, 34, 70],
    [30, 26, 96],
    [52, 40, 58],
    [88, 22, 120],
    [106, 36, 84],
    [138, 28, 140],
    [162, 44, 66],
    [200, 24, 104],
    [220, 38, 78],
    [254, 30, 150],
    [280, 42, 92],
    [318, 26, 118],
    [340, 40, 70],
    [376, 30, 132],
    [402, 44, 86],
    [442, 24, 110],
    [462, 40, 74],
    [498, 32, 126],
    [526, 42, 90],
    [564, 28, 144],
    [588, 52, 80],
  ];
  const near: [number, number, number][] = [
    [10, 46, 46],
    [60, 34, 64],
    [100, 52, 40],
    [160, 30, 72],
    [196, 48, 52],
    [250, 36, 60],
    [292, 54, 44],
    [352, 32, 70],
    [390, 50, 50],
    [446, 34, 66],
    [486, 56, 42],
    [548, 36, 62],
    [590, 50, 48],
  ];
  const ground = 178;
  return (
    <svg viewBox="0 0 640 220" preserveAspectRatio="xMaxYMax slice" aria-hidden className={className}>
      <defs>
        <linearGradient id="bs-sky" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="rgb(var(--blue-soft))" stopOpacity="0" />
          <stop offset="100%" stopColor="rgb(var(--blue-soft))" />
        </linearGradient>
        <linearGradient id="bs-body" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="rgb(var(--surface))" />
          <stop offset="100%" stopColor="rgb(var(--navy-soft))" />
        </linearGradient>
        <linearGradient id="bs-glass" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="rgb(var(--navy-2))" />
          <stop offset="100%" stopColor="rgb(var(--navy))" />
        </linearGradient>
        <linearGradient id="bs-stripe" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stopColor="rgb(var(--blue))" stopOpacity="0" />
          <stop offset="35%" stopColor="rgb(var(--blue))" />
          <stop offset="100%" stopColor="rgb(var(--blue-bright))" />
        </linearGradient>
      </defs>

      <rect width="640" height="220" fill="url(#bs-sky)" />
      <g fill="rgb(var(--navy))" fillOpacity="0.07">
        {far.map(([x, width, height]) => (
          <rect key={x} x={x} y={ground - height} width={width} height={height} rx="2" />
        ))}
      </g>
      <g fill="rgb(var(--navy))" fillOpacity="0.09">
        {near.map(([x, width, height]) => (
          <rect key={x} x={x} y={ground - height} width={width} height={height} rx="2" />
        ))}
      </g>
      <g fill="rgb(var(--blue))" fillOpacity="0.16">
        {[
          [142, 52],
          [150, 64],
          [262, 40],
          [270, 58],
          [382, 60],
          [570, 48],
          [578, 70],
          [94, 72],
        ].map(([x, y]) => (
          <rect key={`${x}-${y}`} x={x} y={y} width="5" height="7" rx="1" />
        ))}
      </g>

      <rect x="0" y={ground} width="640" height="42" fill="rgb(var(--navy))" fillOpacity="0.06" />
      <g stroke="rgb(var(--surface))" strokeWidth="3" strokeLinecap="round">
        {[20, 90, 160, 230, 300, 370, 440, 510, 580].map(x => (
          <line key={x} x1={x} y1={ground + 22} x2={x + 34} y2={ground + 22} />
        ))}
      </g>

      <g transform="translate(250 92)">
        <ellipse cx="160" cy="92" rx="170" ry="7" fill="rgb(var(--navy))" fillOpacity="0.16" />
        <path
          d="M8 22 Q8 6 24 6 H286 Q304 6 312 20 L330 58 Q334 66 334 74 V80 Q334 86 328 86 H14 Q8 86 8 80 Z"
          fill="url(#bs-body)"
          stroke="rgb(var(--navy))"
          strokeOpacity="0.14"
        />
        <path d="M20 16 H284 Q296 16 302 26 L318 54 H20 Z" fill="url(#bs-glass)" />
        <g stroke="rgb(var(--surface))" strokeOpacity="0.55" strokeWidth="2">
          {[62, 104, 146, 188, 230, 268].map(x => (
            <line key={x} x1={x} y1="17" x2={x} y2="53" />
          ))}
        </g>
        <path d="M30 20 H120 L96 50 H30 Z" fill="white" fillOpacity="0.08" />
        <rect x="8" y="60" width="326" height="6" fill="url(#bs-stripe)" />
        <rect x="284" y="58" width="2" height="24" fill="rgb(var(--navy))" fillOpacity="0.2" />
        <rect x="320" y="70" width="12" height="5" rx="2.5" fill="rgb(var(--warning-bright))" />
        {[66, 266].map(cx => (
          <g key={cx}>
            <circle cx={cx} cy="86" r="17" fill="rgb(var(--navy))" />
            <circle cx={cx} cy="86" r="8" fill="rgb(var(--side-fg))" />
            <circle cx={cx} cy="86" r="3" fill="rgb(var(--navy))" />
          </g>
        ))}
      </g>
    </svg>
  );
}
