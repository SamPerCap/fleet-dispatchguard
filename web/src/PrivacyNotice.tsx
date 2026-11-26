import { t } from './i18n/t';

/**
 * Everything that is recorded, and nothing else: it changes with SPEC 0085. Only for whoever asks for it in the sidebar: it does
 * not open by itself.
 */
export function PrivacyNotice({ onClose }: { onClose: () => void }) {
  return (
    <section aria-label={t.app.privacy} className="page card mb-4 flex flex-wrap items-center gap-3 px-4 py-3">
      <p className="min-w-0 flex-1 basis-96 text-meta text-ink-2" role="status">
        {t.privacy.text}
      </p>
      <button type="button" className="btn btn-sm" onClick={onClose}>
        {t.common.close}
      </button>
    </section>
  );
}
