import { ArrowRight, UserRound, Wrench } from 'lucide-react';
import { BRAND } from '../brand';
import { BrandScene, BrandMark } from '../brand/BrandArt';
import { LanguageSwitch } from '../LanguageSwitch';
import { ABOUT_HASH, ENGINEERING_HASH, START_HASH } from '../route';
import { AllocationFlow } from './AllocationFlow';
import { Evidence } from './Evidence';
import { coldStartWait } from './measurements';
import { showcaseText } from './showcaseText';
import { TECHNOLOGIES } from './text';

/** Asks nothing of the server: its ways in are links, and what they open asks (SPEC 0093 R1). */
export function Cover() {
  const { tagline, cover: text, flow } = showcaseText();
  const { wait, measuredOn } = coldStartWait();
  return (
    <div className="flex min-h-screen flex-col">
      <header className="flex items-center gap-3 px-4 py-6 sm:px-12">
        <BrandMark size={40} />
        <span className="min-w-0 flex-1">
          <span className="block text-[17px] font-extrabold leading-6 tracking-tight text-ink sm:text-[22px]">{BRAND.name}</span>
          <span className="block text-xs leading-4 text-ink-3">{tagline}</span>
        </span>
        <LanguageSwitch />
      </header>
      <main className="mx-auto w-full max-w-[110rem] flex-1 px-4 py-8 sm:px-12">
        <h1 className="text-page font-bold text-ink sm:text-display">{text.title}</h1>
        <p className="mt-5 whitespace-pre-line text-module text-ink-2">{text.problem}</p>
        <div className="mt-8 flex flex-wrap gap-3">
          <a href={START_HASH} className="btn btn-primary h-12 px-6 text-module font-semibold">
            {text.tryIt}
            <ArrowRight className="h-5 w-5" aria-hidden />
          </a>
          <a href={ENGINEERING_HASH} className="btn h-12 px-6 text-module font-semibold">
            <Wrench className="h-5 w-5" aria-hidden />
            {text.howItIsBuilt}
          </a>
          <a href={ABOUT_HASH} className="btn h-12 px-6 text-module font-semibold">
            <UserRound className="h-5 w-5" aria-hidden />
            {text.about}
          </a>
        </div>
        <p className="mt-6 text-meta text-ink-3">{text.notes(wait, measuredOn).join(' ')}</p>
        <div className="mt-12">
          <Evidence />
        </div>
        <section aria-labelledby="flow" className="mt-12">
          <h2 id="flow" className="mb-4 text-[22px] font-bold leading-7 text-ink">
            {flow.title}
          </h2>
          <AllocationFlow />
        </section>
        <ul aria-label={text.technologies} className="mt-10 flex flex-wrap gap-2">
          {TECHNOLOGIES.map(technology => (
            <li key={technology} className="chip px-3 py-1 text-meta">
              {technology}
            </li>
          ))}
        </ul>
      </main>
      {/* In its own band: the text above is never drawn over it. */}
      <BrandScene className="h-[160px] w-full [mask-image:linear-gradient(to_bottom,transparent,black_30%)] sm:h-[200px]" />
    </div>
  );
}
