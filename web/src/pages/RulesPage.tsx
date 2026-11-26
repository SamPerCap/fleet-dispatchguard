import { t } from '../i18n/t';
import {
  ArrowDownUp,
  ArrowLeft,
  BedDouble,
  BusFront,
  CalendarClock,
  CalendarX2,
  Gauge,
  House,
  Info,
  Scale,
  ShieldAlert,
  ShieldX,
  type LucideIcon,
} from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { api } from '../api';
import type { Tone } from '../calendar';
import type { RuleGroup, RuleKind } from '../types';
import { EmptyState, ErrorBox, IconBadge, PageHeader, SkeletonLines, Stat, type IconTone } from '../ui';

export const RULE_KIND: Record<RuleKind, { text: string; tone: Tone; hint: string; icon: LucideIcon; cls: string }> = {
  Blocking: {
    get text() { return t.rules.blocks; },
    tone: 'danger',
    get hint() { return t.rules.blocksWhat; },
    icon: ShieldX,
    cls: 'bg-critical-soft text-critical-ink',
  },
  Warning: {
    get text() { return t.rules.warns; },
    tone: 'warn',
    get hint() { return t.rules.warnsWhat; },
    icon: ShieldAlert,
    cls: 'bg-warning-soft text-warning-ink',
  },
  Ranking: {
    get text() { return t.rules.ranks; },
    tone: 'ok',
    get hint() { return t.rules.ranksWhat; },
    icon: ArrowDownUp,
    cls: 'bg-blue-soft text-blue-ink',
  },
  Informative: {
    get text() { return t.rules.informative; },
    tone: 'muted',
    get hint() { return t.rules.informativeWhat; },
    icon: Info,
    cls: 'bg-muted text-ink-2',
  },
};

const KIND_STAT_TONE: Record<RuleKind, IconTone> = { Blocking: 'critical', Warning: 'warning', Ranking: 'blue', Informative: 'neutral' };
const KIND_STAT: Record<RuleKind, string> = {
  get Blocking() { return t.rules.blockingRules; },
  get Warning() { return t.rules.warningRules; },
  get Ranking() { return t.rules.rankingRules; },
  get Informative() { return t.rules.informativeRules; },
};

/** Keys of `RulesCatalog`. */
export const GROUP_ICON: Record<string, { icon: LucideIcon; tone: IconTone }> = {
  driving: { icon: Gauge, tone: 'blue' },
  rest: { icon: BedDouble, tone: 'blue' },
  later: { icon: CalendarClock, tone: 'blue' },
  return: { icon: House, tone: 'blue' },
  overlap: { icon: CalendarX2, tone: 'blue' },
  vehicle: { icon: BusFront, tone: 'blue' },
  ranking: { icon: ArrowDownUp, tone: 'neutral' },
};

function KindTag({ kind }: { kind: RuleKind }) {
  const look = RULE_KIND[kind] ?? RULE_KIND.Informative;
  const Icon = look.icon;
  return (
    <span
      className={`inline-flex h-6 shrink-0 items-center gap-1 rounded-full pl-1.5 pr-2 text-xs font-semibold ${look.cls}`}
      title={look.hint}
    >
      <Icon className="h-3.5 w-3.5" aria-hidden />
      {look.text}
    </span>
  );
}

export function RulesPage({ focusGroup, onBack }: { focusGroup?: string | null; onBack?: () => void }) {
  const [groups, setGroups] = useState<RuleGroup[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let live = true;
    api
      .rules()
      .then(loaded => {
        if (live) {
          setGroups(loaded);
          setError(null);
        }
      })
      .catch(e => live && setError((e as Error).message));
    return () => {
      live = false;
    };
  }, []);
  const headings = useRef(new Map<string, HTMLHeadingElement>());
  useEffect(() => {
    if (!groups || !focusGroup) return;
    const heading = headings.current.get(focusGroup);
    heading?.scrollIntoView({ block: 'start' });
    heading?.focus({ preventScroll: true });
  }, [groups, focusGroup]);
  const all = groups?.flatMap(g => g.Rules) ?? [];
  const count = (kind: RuleKind) => all.filter(r => r.Kind === kind).length;

  return (
    <div className="page">
      {onBack && (
        <button type="button" className="btn btn-link -ml-2 mb-2" onClick={onBack}>
          <ArrowLeft className="h-4 w-4" aria-hidden />
          {t.engine.title}
        </button>
      )}
      <PageHeader
        title={t.app.rules}
        icon={Scale}
        subtitle={t.rules.intro}
      />
      {groups && (
        <div className="mb-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-4" role="group" aria-label={t.rules.byKind}>
          {(['Blocking', 'Warning', 'Ranking', 'Informative'] as RuleKind[]).map(kindKey => {
            const kind = RULE_KIND[kindKey];
            return (
              <Stat
                key={kindKey}
                icon={kind.icon}
                tone={KIND_STAT_TONE[kindKey]}
                label={KIND_STAT[kindKey]}
                value={count(kindKey)}
                foot={kind.hint}
              />
            );
          })}
        </div>
      )}
      {error && <ErrorBox>{t.rules.failed(error)}</ErrorBox>}
      {!groups && !error && (
        <div className="card p-6">
          <SkeletonLines lines={8} label={t.rules.loading} />
        </div>
      )}
      {groups?.length === 0 && (
        <div className="card">
          <EmptyState icon={Scale} title={t.common.noRules} />
        </div>
      )}
      {/* The prose of each rule stays under about 80 characters per line. */}
      <div className="grid items-start gap-6 2xl:grid-cols-2">
        {groups?.map(group => {
          const look = GROUP_ICON[group.Key] ?? { icon: Scale, tone: 'neutral' as IconTone };
          return (
            <section
              key={group.Key}
              aria-labelledby={`rules-${group.Key}`}
              className={`module scroll-mt-6 ${focusGroup === group.Key ? 'ring-2 ring-blue' : ''}`}
            >
              <header className="mb-2 flex items-center gap-3">
                <IconBadge icon={look.icon} tone={look.tone} size="sm" />
                <h2
                  id={`rules-${group.Key}`}
                  tabIndex={-1}
                  ref={element => {
                    if (element) headings.current.set(group.Key, element);
                    else headings.current.delete(group.Key);
                  }}
                  className="module-title scroll-mt-6 outline-none"
                >
                  {group.Title}
                </h2>
                <span className="text-meta text-ink-3">
                  {t.rules.count(group.Rules.length)}
                </span>
              </header>
              <ul className="divide-y divide-line">
                {group.Rules.map(rule => (
                  <li key={rule.Id} className="grid gap-x-6 gap-y-2 py-4 last:pb-0 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-semibold text-ink">{rule.Name}</span>
                        <KindTag kind={rule.Kind} />
                      </div>
                      <p className="mt-1 text-meta text-ink-2">{rule.Explanation}</p>
                      <p className="mt-0.5 text-xs text-ink-3">{rule.Reference}</p>
                    </div>
                    <div
                      className="justify-self-start rounded-md bg-sunken px-3 py-2 text-data font-bold text-ink sm:justify-self-end sm:text-right"
                      aria-label={t.rules.figure(rule.Figure)}
                    >
                      {rule.Figure}
                    </div>
                  </li>
                ))}
              </ul>
            </section>
          );
        })}
      </div>
    </div>
  );
}
