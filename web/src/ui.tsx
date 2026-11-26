import { t } from './i18n/t';
// Tokens live in index.css; guide in docs/design-system.md.
import * as Dialog from '@radix-ui/react-dialog';
import {
  AlertOctagon,
  AlertTriangle,
  CalendarClock,
  CheckCheck,
  CircleCheck,
  CircleDashed,
  CircleDot,
  Clock3,
  Info,
  RefreshCw,
  ShieldAlert,
  X,
  type LucideIcon,
} from 'lucide-react';
import { avatarTone, initials, type AvatarTone } from './people';
import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { decimalsOf, NUMBER_TWEEN_MS, prefersReducedMotion, tween } from './motion';
import { placePopover, type Placement } from './popover';
import type { Tone } from './calendar';
import { STATUS_TEXT, type StatusKey } from './status';
import { locale } from './language';

/** `flush`: the content sets its own margins, so that a sticky title or button bar reaches the edge; the focus never stays under them. */
export function Sheet({
  open,
  onClose,
  title,
  children,
  wide,
  flush,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  wide?: boolean;
  flush?: boolean;
}) {
  return (
    <Dialog.Root
      open={open}
      onOpenChange={nextOpen => {
        if (!nextOpen) onClose();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-navy/20" />
        <Dialog.Content
          aria-describedby={undefined}
          className={`fixed right-0 top-0 z-40 flex h-full ${wide ? 'w-[min(clamp(760px,42vw,960px),100vw)]' : 'w-[min(clamp(560px,32vw,680px),100vw)]'} flex-col bg-surface shadow-pop`}
        >
          <header className="flex items-center justify-between gap-3 border-b border-line px-6 py-4">
            <Dialog.Title className="min-w-0 text-module font-semibold [overflow-wrap:anywhere]">{title}</Dialog.Title>
            <Dialog.Close className="btn btn-text btn-icon shrink-0" aria-label={t.ui.closePanel}>
              <X className="h-4 w-4" aria-hidden />
            </Dialog.Close>
          </header>
          <div className={`flex-1 overflow-auto ${flush ? 'scroll-pb-28 scroll-pt-12' : 'px-6 py-4'}`}>{children}</div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

// Literal classes: Tailwind only keeps those it finds written in full.
const TONE: Record<Tone, string> = { ok: 'tone-ok', warn: 'tone-warn', danger: 'tone-danger', muted: 'tone-muted' };
/** For what is NOT a status: statuses use StatusBadge. */
export const Badge = ({ tone, children }: { tone: Tone; children: ReactNode }) => (
  <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${TONE[tone]}`}>{children}</span>
);

/** Never colour alone. */
const STATUS_STYLE: Record<StatusKey, { icon: LucideIcon; cls: string }> = {
  planned: { icon: CalendarClock, cls: 'bg-muted text-ink-2' },
  assigned: { icon: CircleCheck, cls: 'bg-success-soft text-success-ink' },
  running: { icon: CircleDot, cls: 'bg-navy text-white' },
  completed: { icon: CheckCheck, cls: 'bg-muted text-ink-3' },
  uncovered: { icon: AlertOctagon, cls: 'bg-critical-soft text-critical-ink' },
  forced: { icon: ShieldAlert, cls: 'bg-warning-soft text-warning-ink' },
  partial: { icon: CircleDashed, cls: 'bg-warning-soft text-warning-ink' },
};

/** The only status component of the web. */
export function StatusBadge({ status, label, title }: { status: StatusKey; label?: string; title?: string }) {
  const { icon: Icon, cls } = STATUS_STYLE[status];
  const text = STATUS_TEXT[status];
  return (
    <span
      className={`inline-flex h-6 shrink-0 items-center gap-1 whitespace-nowrap rounded-full pl-1.5 pr-2 text-xs font-medium ${cls}`}
      title={title ?? text.definition}
      data-status={status}
    >
      <Icon className="h-3.5 w-3.5" aria-hidden />
      {label ?? text.label}
    </span>
  );
}

export function SeverityTag({ severity }: { severity: 'Urgent' | 'Warning' }) {
  return severity === 'Urgent' ? (
    <span className="inline-flex h-6 items-center gap-1 rounded-full bg-critical-soft pl-1.5 pr-2 text-xs font-semibold text-critical-ink">
      <AlertOctagon className="h-3.5 w-3.5" aria-hidden />
      {t.ui.urgent}
    </span>
  ) : (
    <span className="inline-flex h-6 items-center gap-1 rounded-full bg-warning-soft pl-1.5 pr-2 text-xs font-semibold text-warning-ink">
      <AlertTriangle className="h-3.5 w-3.5" aria-hidden />
      {t.common.warning}
    </span>
  );
}

export const Skeleton = ({ className = '' }: { className?: string }) => <div aria-hidden className={`skeleton ${className}`} />;

export function SkeletonLines({ lines = 3, label = t.common.loading }: { lines?: number; label?: string }) {
  return (
    <div role="status" aria-live="polite" className="space-y-3">
      <span className="sr-only">{label}…</span>
      {Array.from({ length: lines }, (_, index) => (
        <Skeleton key={index} className={`h-4 ${index % 3 === 2 ? 'w-2/3' : 'w-full'}`} />
      ))}
    </div>
  );
}

export function EmptyState({
  icon: Icon = Info,
  title,
  children,
  tone = 'neutral',
}: {
  icon?: LucideIcon;
  title: string;
  children?: ReactNode;
  tone?: 'neutral' | 'ok';
}) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 px-4 py-8 text-center">
      <span
        className={`grid h-10 w-10 place-items-center rounded-full ${tone === 'ok' ? 'bg-success-soft text-success-ink' : 'bg-muted text-ink-2'}`}
      >
        <Icon className="h-5 w-5" aria-hidden />
      </span>
      <p className="font-medium text-ink">{title}</p>
      {children && <p className="max-w-sm text-meta text-ink-2">{children}</p>}
    </div>
  );
}

export function ErrorState({
  title = t.ui.failed,
  children,
  onRetry,
}: {
  title?: string;
  children?: ReactNode;
  onRetry?: () => void;
}) {
  return (
    <div role="alert" className="flex flex-wrap items-center gap-3 rounded-md bg-critical-soft px-4 py-3 text-critical-ink">
      <AlertOctagon className="h-4 w-4 shrink-0" aria-hidden />
      <p className="min-w-0 flex-1">
        <b className="font-semibold">{title}.</b> {children}
      </p>
      {onRetry && (
        <button type="button" className="btn btn-sm" onClick={onRetry}>
          <RefreshCw className="h-4 w-4" aria-hidden />
          {t.common.retry}
        </button>
      )}
    </div>
  );
}

/** A region with that name: the E2E tests look it up that way. */
export const Section = ({ title, icon, children, aside }: { title: string; icon: LucideIcon; children: ReactNode; aside?: ReactNode }) => (
  <section aria-label={title} className="space-y-3">
    <h3 className="flex items-center gap-2 text-meta font-semibold text-ink">
      <IconBadge icon={icon} tone="neutral" size="sm" />
      {title}
      <span className="flex-1" />
      {aside}
    </h3>
    {children}
  </section>
);

export function ErrorBox({ children }: { children: ReactNode }) {
  return (
    <div role="alert" className="rounded-md bg-critical-soft px-4 py-3 text-critical-ink">
      {children}
    </div>
  );
}

export type ModuleTone = 'light' | 'navy' | 'critical';
const MODULE_TONE: Record<ModuleTone, string> = { light: 'module', navy: 'module surface-navy', critical: 'module surface-critical' };

/** `stale`: the module shows its last good value because the reload failed. */
export function Module({
  title,
  label,
  actions,
  children,
  className = '',
  sub,
  icon,
  iconTone = 'blue',
  tone = 'light',
  stale,
}: {
  title: string;
  label?: string;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
  sub?: ReactNode;
  icon?: LucideIcon;
  iconTone?: IconTone;
  tone?: ModuleTone;
  stale?: string | null;
}) {
  return (
    <section aria-label={label ?? title} className={`${MODULE_TONE[tone]} ${className}`}>
      <header className="mb-4 flex flex-wrap items-center gap-x-3 gap-y-2">
        {icon && <IconBadge icon={icon} tone={tone === 'navy' ? 'onNavy' : iconTone} size="sm" />}
        <h2 className="module-title">{title}</h2>
        {sub}
        <span className="flex-1" />
        {actions}
      </header>
      {stale && <StaleNote text={stale} onNavy={tone === 'navy'} />}
      {children}
    </section>
  );
}

export function StaleNote({ text, onNavy }: { text: string; onNavy?: boolean }) {
  return (
    <p
      role="status"
      className={`-mt-1 mb-3 inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ${onNavy ? 'bg-warning-bright/15 text-warning-bright' : 'bg-warning-soft text-warning-ink'}`}
    >
      <Clock3 className="h-3.5 w-3.5" aria-hidden />
      {text}
    </p>
  );
}

/** All tones are AA on their background. */
export type IconTone = 'blue' | 'success' | 'warning' | 'critical' | 'navy' | 'neutral' | 'onNavy';
const ICON_TONE: Record<IconTone, string> = {
  blue: 'bg-blue-soft text-blue-ink',
  success: 'bg-success-soft text-success-ink',
  warning: 'bg-warning-soft text-warning-ink',
  critical: 'bg-critical-soft text-critical-ink',
  navy: 'bg-navy text-white',
  neutral: 'bg-muted text-ink-2',
  onNavy: 'bg-white/10 text-white',
};
const ICON_SIZE = {
  sm: 'h-9 w-9 [&>svg]:h-[18px] [&>svg]:w-[18px]',
  md: 'h-11 w-11 [&>svg]:h-5 [&>svg]:w-5',
  lg: 'h-12 w-12 [&>svg]:h-6 [&>svg]:w-6',
} as const;

/** Always decorative: the text next to it says the same. */
export function IconBadge({
  icon: Icon,
  tone = 'blue',
  size = 'md',
}: {
  icon: LucideIcon;
  tone?: IconTone;
  size?: keyof typeof ICON_SIZE;
}) {
  return (
    <span aria-hidden className={`grid shrink-0 place-items-center rounded-full ${ICON_TONE[tone]} ${ICON_SIZE[size]}`}>
      <Icon />
    </span>
  );
}

const AVATAR_TONE: Record<AvatarTone, string> = {
  blue: 'bg-blue-soft text-blue-ink',
  success: 'bg-success-soft text-success-ink',
  warning: 'bg-warning-soft text-warning-ink',
  navy: 'bg-navy-soft text-ink',
};
/** aria-hidden because the full name is next to it. */
export function Avatar({ name, size = 'md', toneKey }: { name: string | null | undefined; size?: 'sm' | 'md'; toneKey?: string | number }) {
  return (
    <span
      aria-hidden
      className={`grid shrink-0 place-items-center rounded-full font-semibold ${AVATAR_TONE[avatarTone(toneKey ?? name ?? '')]} ${size === 'sm' ? 'h-7 w-7 text-xs' : 'h-9 w-9 text-meta'}`}
    >
      {initials(name)}
    </span>
  );
}

/** For screen readers the dot is the "Semáforo …" image. */
export function StatusPill({ tone, text, light }: { tone: 'ok' | 'warn' | 'danger'; text: string; light?: string }) {
  const cls =
    tone === 'ok'
      ? 'bg-success-soft text-success-ink'
      : tone === 'warn'
        ? 'bg-warning-soft text-warning-ink'
        : 'bg-critical-soft text-critical-ink';
  const dot = tone === 'ok' ? 'bg-success' : tone === 'warn' ? 'bg-warning' : 'bg-critical';
  return (
    <span className={`inline-flex h-8 items-center gap-2 whitespace-nowrap rounded-full pl-3 pr-3.5 text-meta font-semibold ${cls}`}>
      <span
        role={light ? 'img' : undefined}
        aria-label={light ? t.ui.light(light) : undefined}
        className={`h-2.5 w-2.5 rounded-full ${dot}`}
      />
      {text}
    </span>
  );
}

/** Painted in a portal on <body> with a fixed position, so no ancestor with overflow or z-index clips it. */
export function Hint({ label, children }: { label: string; children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const [place, setPlace] = useState<Placement | null>(null);
  const button = useRef<HTMLButtonElement>(null);
  const pop = useRef<HTMLSpanElement>(null);
  const id = useId();

  const reposition = useCallback(() => {
    const buttonElement = button.current,
      popElement = pop.current;
    if (!buttonElement || !popElement) return;
    setPlace(
      placePopover(
        buttonElement.getBoundingClientRect(),
        { width: popElement.offsetWidth, height: popElement.offsetHeight },
        { width: innerWidth, height: innerHeight },
      ),
    );
  }, []);
  useLayoutEffect(() => {
    if (open) reposition();
    else setPlace(null);
  }, [open, reposition]);
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => {
      if (!button.current?.contains(event.target as Node)) setOpen(false);
    };
    const esc = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };
    addEventListener('scroll', reposition, true);
    addEventListener('resize', reposition);
    document.addEventListener('pointerdown', outside);
    document.addEventListener('keydown', esc);
    return () => {
      removeEventListener('scroll', reposition, true);
      removeEventListener('resize', reposition);
      document.removeEventListener('pointerdown', outside);
      document.removeEventListener('keydown', esc);
    };
  }, [open, reposition]);

  return (
    <>
      <button
        ref={button}
        type="button"
        aria-label={t.ui.whatIs(label)}
        aria-expanded={open}
        aria-describedby={open ? id : undefined}
        className="hint relative inline-flex rounded-full text-ink-3 hover:text-ink after:absolute after:left-1/2 after:top-1/2 after:h-8 after:w-8 after:-translate-x-1/2 after:-translate-y-1/2 after:content-['']"
        onClick={() => setOpen(o => !o)}
        onBlur={() => setOpen(false)}
      >
        <Info className="h-4 w-4" aria-hidden />
      </button>
      {open &&
        createPortal(
          <span
            ref={pop}
            id={id}
            role="tooltip"
            className="pointer-events-none fixed z-[60] w-72 rounded-md bg-navy px-3 py-2 text-xs font-normal normal-case leading-5 tracking-normal text-white shadow-pop"
            style={place ? { left: place.left, top: place.top } : { left: 0, top: 0, visibility: 'hidden' }}
          >
            {children}
          </span>,
          document.body,
        )}
    </>
  );
}

export function PageHeader({
  title,
  subtitle,
  icon,
  children,
}: {
  title: string;
  subtitle?: ReactNode;
  icon?: LucideIcon;
  children?: ReactNode;
}) {
  return (
    <header className="mb-6 flex flex-wrap items-center gap-x-4 gap-y-3">
      {icon && <IconBadge icon={icon} tone="blue" size="lg" />}
      <div className="min-w-0 flex-1">
        <h1 className="text-page font-bold text-ink">{title}</h1>
        {subtitle && <p className="mt-0.5 text-meta text-ink-2">{subtitle}</p>}
      </div>
      {children && <div className="flex flex-wrap items-center gap-2">{children}</div>}
    </header>
  );
}

export const Toolbar = ({ children, label }: { children: ReactNode; label?: string }) => (
  <div role={label ? 'toolbar' : undefined} aria-label={label} className="card mb-4 flex flex-wrap items-center gap-2 px-4 py-3">
    {children}
  </div>
);

export function Stat({
  icon,
  tone = 'blue',
  label,
  value,
  foot,
  className = '',
}: {
  icon?: LucideIcon;
  tone?: IconTone;
  label: ReactNode;
  value: ReactNode;
  foot?: ReactNode;
  className?: string;
}) {
  return (
    <div className={`card flex min-w-0 items-center gap-3 px-4 py-3 ${className}`}>
      {icon && <IconBadge icon={icon} tone={tone} size="md" />}
      <div className="min-w-0 flex-1 break-words">
        <p className="text-page font-bold text-ink">{typeof value === 'number' ? <AnimatedNumber value={value} /> : value}</p>
        <p className="text-meta text-ink-2">{label}</p>
        {foot && <p className="text-xs text-ink-3">{foot}</p>}
      </div>
    </div>
  );
}

/** Decorative: whoever shows it says it in text too. */
export const ConflictMark = () => (
  <span aria-hidden className="grid h-4 w-4 shrink-0 place-items-center rounded-full border border-warning bg-warning-soft text-warning-ink">
    <AlertTriangle className="h-2.5 w-2.5" />
  </span>
);

/**
 * The sample looks EXACTLY like the bar (not like the `StatusBadge`), and its text is the one in the taxonomy. `agenda`: the bars are
 * drawn over the absences of their row.
 */
export function BarLegend({ nowText, agenda }: { nowText?: string; agenda?: boolean }) {
  const items: { cls: string; text: string }[] = [
    { cls: 'bg-navy-tint', text: STATUS_TEXT.assigned.label },
    { cls: 'bg-navy', text: STATUS_TEXT.running.label },
    { cls: 'bg-muted', text: STATUS_TEXT.completed.label },
    { cls: 'border border-dashed border-critical bg-critical-soft', text: STATUS_TEXT.uncovered.label },
    { cls: 'bg-navy-tint shadow-[inset_3px_0_0_rgb(var(--warning))]', text: STATUS_TEXT.forced.label },
  ];
  return (
    <ul className="flex flex-wrap items-center gap-x-4 gap-y-1.5 text-xs text-ink-2" aria-label={t.common.legend}>
      {items.map(item => (
        <li key={item.text} className="inline-flex items-center gap-1.5">
          <span aria-hidden className={`h-3 w-5 rounded-[4px] ${item.cls}`} />
          {item.text}
        </li>
      ))}
      {agenda && (
        <>
          <li className="inline-flex items-center gap-1.5">
            <span aria-hidden className="agenda-band h-3 w-5 rounded-[4px]" />
            {t.ui.unavailableAgenda}
          </li>
          <li className="inline-flex items-center gap-1.5">
            <ConflictMark />
            {t.ui.conflictAgenda}
          </li>
        </>
      )}
      {nowText && (
        <li className="inline-flex items-center gap-1.5">
          <span aria-hidden className="h-3 w-0.5 rounded bg-blue" />
          {nowText}
        </li>
      )}
    </ul>
  );
}

const localNumber = (value: number) => value.toLocaleString(locale());
/** The first paint and a refresh without changes are not animated. Meanwhile screen readers only read the final value. */
export function AnimatedNumber({ value, format = localNumber }: { value: number; format?: (value: number) => string }) {
  const [shown, setShown] = useState(value);
  const current = useRef(value); // What is shown now, also halfway through a transition.
  useEffect(() => {
    const from = current.current;
    if (from === value) return;
    if (prefersReducedMotion() || !Number.isFinite(from) || !Number.isFinite(value)) {
      current.current = value;
      setShown(value);
      return;
    }
    const decimals = Math.max(decimalsOf(from), decimalsOf(value));
    const start = performance.now();
    let frame = requestAnimationFrame(function step(timestamp) {
      const progress = (timestamp - start) / NUMBER_TWEEN_MS;
      const interpolated = tween(from, value, progress, decimals);
      current.current = interpolated;
      setShown(interpolated);
      if (progress < 1) frame = requestAnimationFrame(step);
    });
    return () => cancelAnimationFrame(frame);
  }, [value]);
  if (shown === value) return <>{format(value)}</>;
  return (
    <>
      <span aria-hidden>{format(shown)}</span>
      <span className="sr-only">{format(value)}</span>
    </>
  );
}
