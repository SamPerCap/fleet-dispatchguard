import { ArrowLeft, ArrowRight } from 'lucide-react';
import { useEffect } from 'react';
import { BRAND } from '../brand';
import { BrandMark } from '../brand/BrandArt';
import { LanguageSwitch } from '../LanguageSwitch';
import { START_HASH } from '../route';
import { Evidence } from './Evidence';
import { showcaseText } from './showcaseText';
import type { EngineeringSection } from './text';

/** Asks nothing of the server, like the cover. */
export function EngineeringPage() {
  const { tagline, engineering: text } = showcaseText();
  // It is another page for the reader, not a place further down the one that brought here.
  // In a block: browsers now return a Promise from scrollTo, and React would call it as the cleanup (TypeError on Back).
  useEffect(() => {
    scrollTo({ top: 0 });
  }, []);
  return (
    <div className="min-h-screen">
      <header className="on-navy bg-gradient-to-br from-navy to-navy-2 px-6 py-10 text-white sm:px-12">
        <div className="mx-auto max-w-screen-2xl">
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
          <p className="mt-3 max-w-2xl text-module text-on-navy-2">{text.intro}</p>
          <a
            href={START_HASH}
            className="mt-6 inline-flex h-12 items-center gap-2 rounded-md bg-blue px-6 text-module font-semibold text-white hover:bg-blue-ink"
          >
            {showcaseText().cover.tryIt}
            <ArrowRight className="h-5 w-5" aria-hidden />
          </a>
        </div>
      </header>
      <main className="mx-auto max-w-screen-2xl space-y-10 px-6 py-10 sm:px-12">
        <Evidence />
        {text.sections.map(section => (
          <Section key={section.id} section={section} />
        ))}
      </main>
    </div>
  );
}

function Section({ section }: { section: EngineeringSection }) {
  return (
    <section aria-labelledby={section.id}>
      <h2 id={section.id} className="text-[22px] font-bold leading-7 text-ink">
        {section.title}
      </h2>
      <p className="mt-2 max-w-3xl text-module text-ink-2">{section.intro}</p>
      <ul className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {section.points.map(point => (
          <li key={point.title} className="module">
            <h3 className="module-title">{point.title}</h3>
            <p className="mt-2 text-data text-ink-2">{point.why}</p>
          </li>
        ))}
      </ul>
    </section>
  );
}
