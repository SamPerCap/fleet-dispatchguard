import { ArrowLeft, ArrowRight, ExternalLink } from 'lucide-react';
import { useEffect } from 'react';
import { BRAND } from '../brand';
import { BrandMark } from '../brand/BrandArt';
import { LanguageSwitch } from '../LanguageSwitch';
import { START_HASH } from '../route';
import { AUTHOR } from './author';
import { REPOSITORY_URL } from './repository';
import { showcaseText } from './showcaseText';
import type { Point, TechnologyGroup } from './text';

/** Who made it and how AI was used: only what the author wrote or approved (SPEC 0096 R4, SPEC 0100 R1). Asks nothing of the server. */
export function AboutPage() {
  const { tagline, about: text, cover } = showcaseText();
  // In a block: browsers now return a Promise from scrollTo, and React would call it as the cleanup (TypeError on Back).
  useEffect(() => {
    scrollTo({ top: 0 });
  }, []);
  return (
    <div className="min-h-screen">
      <header className="on-navy bg-gradient-to-br from-navy to-navy-2 px-4 py-10 text-white sm:px-12">
        <div className="mx-auto max-w-[110rem]">
          <div className="flex items-center justify-between gap-3">
            <a href="#" className="inline-flex h-11 items-center gap-2 text-data font-medium text-side-fg hover:text-white">
              <ArrowLeft className="h-4 w-4" aria-hidden />
              {text.back}
            </a>
            <LanguageSwitch tone="navy" />
          </div>
          <p className="mt-4 flex items-center gap-3 text-meta text-side-muted">
            <BrandMark size={32} />
            {BRAND.name} · {tagline}
          </p>
          <h1 className="mt-4 text-page font-bold sm:text-display">{text.title}</h1>
          <p className="mt-3 text-module font-semibold text-white">{AUTHOR.name}</p>
          {AUTHOR.headline && <p className="mt-1 text-module text-on-navy-2">{AUTHOR.headline}</p>}
          <nav aria-label={text.contact} className="mt-4 flex flex-wrap gap-3">
            {AUTHOR.links.map(link => (
              <ExternalButton key={link.url} href={link.url}>
                {link.name}
              </ExternalButton>
            ))}
          </nav>
        </div>
      </header>
      <main className="mx-auto max-w-[110rem] space-y-10 px-4 py-10 sm:px-12">
        <section className="space-y-4">
          {text.story.map(paragraph => (
            <p key={paragraph} className="text-module text-ink-2">
              {paragraph}
            </p>
          ))}
        </section>
        <section aria-labelledby="ai">
          <h2 id="ai" className="text-[22px] font-bold leading-7 text-ink">
            {text.ai.title}
          </h2>
          <p className="mt-2 text-module text-ink-2">{text.ai.intro}</p>
          <Points points={text.ai.parts} wide />
        </section>
        <section aria-labelledby="architecture">
          <h2 id="architecture" className="text-[22px] font-bold leading-7 text-ink">
            {text.architecture.title}
          </h2>
          <p className="mt-2 text-module text-ink-2">{text.architecture.intro}</p>
          <Points points={text.architecture.points} />
          <h3 className="mt-8 text-module font-semibold text-ink">{cover.technologies}</h3>
          <Technologies groups={text.architecture.technologies} />
        </section>
        <section aria-labelledby="code" className="module">
          <h2 id="code" className="module-title">
            {text.code.title}
          </h2>
          {REPOSITORY_URL ? (
            <a href={REPOSITORY_URL} className="mt-2 inline-block text-data font-medium text-blue-ink underline" target="_blank" rel="noopener noreferrer">
              {text.code.link}
            </a>
          ) : (
            <p className="mt-2 text-data text-ink-2">{text.code.pending}</p>
          )}
        </section>
        <a href={START_HASH} className="btn btn-primary h-12 px-6 text-module font-semibold">
          {cover.tryIt}
          <ArrowRight className="h-5 w-5" aria-hidden />
        </a>
      </main>
    </div>
  );
}

/** Four short cards fit one row on a wide screen; six read better in rows of three. */
function Points({ points, wide = false }: { points: Point[]; wide?: boolean }) {
  return (
    <ul className={`mt-4 grid gap-4 sm:grid-cols-2 ${wide ? 'xl:grid-cols-4' : 'lg:grid-cols-3'}`}>
      {points.map(point => (
        <li key={point.title} className="module">
          <h3 className="module-title">{point.title}</h3>
          <p className="mt-2 text-data text-ink-2">{point.why}</p>
        </li>
      ))}
    </ul>
  );
}

function Technologies({ groups }: { groups: TechnologyGroup[] }) {
  return (
    <div className="mt-3 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      {groups.map(group => (
        <section key={group.group} className="module" aria-label={group.group}>
          <h4 className="module-title">{group.group}</h4>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-data text-ink-2">
            {group.items.map(item => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

function ExternalButton({ href, children }: { href: string; children: string }) {
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      className="inline-flex h-11 items-center gap-2 rounded-md bg-white/10 px-4 text-data font-medium text-white hover:bg-white/20"
    >
      {children}
      <ExternalLink className="h-4 w-4" aria-hidden />
    </a>
  );
}
