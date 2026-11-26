import { dateOf, EVIDENCE } from './measurements';
import { sourceUrl } from './repository';
import { showcaseText } from './showcaseText';

/** Figures with their method, date and what reproduces them, instead of counters (SPEC 0096 R2). */
export function Evidence() {
  const text = showcaseText().evidence;
  return (
    <section aria-labelledby="evidence">
      <h2 id="evidence" className="text-[22px] font-bold leading-7 text-ink">
        {text.title}
      </h2>
      <p className="mt-2 max-w-3xl text-module text-ink-2">{text.intro}</p>
      <ul className="mt-4 grid gap-4 lg:grid-cols-3">
        {EVIDENCE.map(source => {
          const item = text.items[source.id];
          const link = sourceUrl(source.file);
          return (
            <li key={source.id} className="module flex flex-col">
              <p className="text-page font-bold text-ink">{item.figure}</p>
              <p className="mt-1 text-data font-semibold text-ink">{item.claim}</p>
              <p className="mt-3 text-data text-ink-2">{item.method}</p>
              <p className="mt-3 text-meta text-ink-3">{text.measuredOn(dateOf(source.measuredOn))}</p>
              <details className="mt-3 text-meta text-ink-2">
                <summary className="cursor-pointer font-medium text-blue-ink">{text.reproduce}</summary>
                <code className="mt-2 block break-all rounded-md bg-sunken p-2 text-xs">{source.command}</code>
                <code className="mt-2 block break-all text-xs">
                  {source.file} · {source.member}
                </code>
                {link && (
                  <a href={link} className="mt-2 inline-block underline" target="_blank" rel="noopener noreferrer">
                    {text.readTheCode}
                  </a>
                )}
              </details>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
