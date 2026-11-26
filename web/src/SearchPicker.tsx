import { t } from './i18n/t';
import { Check, ChevronDown, X } from 'lucide-react';
import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { optionAfterKey, optionsNote, searchItems, type PickerItem, type PickerMatches } from './pickerSearch';
import { placeList, type ListPlacement } from './popover';

const NOTHING: PickerMatches = { suggested: [], matches: [], blocked: [], total: 0 };

/** Offered first when nothing has been typed yet. */
export interface SuggestedItems {
  label: string;
  others: string;
  ids: number[];
}

export interface PickerTexts {
  placeholder: string;
  singular: string;
  plural: string;
}

/** The top layer is the only place where neither the scroll of a panel clips the list nor its stacking order covers it. */
function showInTopLayer(element: HTMLElement | null) {
  if (element && 'showPopover' in element && !element.matches(':popover-open')) element.showPopover();
}

/** The top layer ignores the scroll containers, so the list would stay on screen without its field. */
function isScrolledAway(field: HTMLElement): boolean {
  const { top, bottom } = field.getBoundingClientRect();
  // Not <body>: dialogs lock its scroll, but being fixed they are not clipped by it, however short the page behind is.
  for (let parent = field.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
    if (getComputedStyle(parent).overflowY === 'visible') continue;
    const bounds = parent.getBoundingClientRect();
    if (bottom <= bounds.top || top >= bounds.bottom) return true;
  }
  return false;
}

const samePlacement = (a: ListPlacement | null, b: ListPlacement) =>
  a !== null && a.left === b.left && a.width === b.width && a.maxHeight === b.maxHeight && a.top === b.top && a.bottom === b.bottom;

const Tag = ({ children }: { children: string }) => (
  <span className="chip mr-1">{children}</span>
);

/**
 * Combobox + listbox pattern over items already loaded. Only choosing an option, or emptying the field if it is `clearable`, changes
 * `value`; the typed text never does (SPEC 0077). `items` is indexed by identity: memoise it. `showDetail`: the detail of the chosen
 * item goes under the field.
 */
export function SearchPicker({
  label,
  value,
  items,
  texts,
  onChange,
  suggested,
  required,
  clearable = true,
  showDetail = true,
  inputClass = '',
  autoFocus,
}: {
  label: string;
  value: number | null;
  items: PickerItem[];
  texts: PickerTexts;
  onChange: (id: number | null) => void;
  suggested?: SuggestedItems;
  required?: boolean;
  clearable?: boolean;
  showDetail?: boolean;
  inputClass?: string;
  autoFocus?: boolean;
}) {
  const id = useId();
  const listId = `${id}-list`;
  const detailId = `${id}-detail`;
  const optionId = (index: number) => `${id}-option-${index}`;
  const input = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false);
  /** null = not being edited: the field shows the chosen item. */
  const [draft, setDraft] = useState<string | null>(null);
  const [active, setActive] = useState(-1);
  const [place, setPlace] = useState<ListPlacement | null>(null);

  const chosen = useMemo(() => items.find(item => item.id === value) ?? null, [items, value]);
  const chosenName = chosen?.label ?? (value === null ? '' : `#${value}`);
  const query = draft ?? '';
  const search = (text: string) => searchItems(items, text, suggested?.ids);
  const found = open ? search(query) : NOTHING;
  const options = [...found.suggested, ...found.matches, ...found.blocked];
  const current = Math.min(active, options.length - 1);
  const note = optionsNote(options.length, found.total, query, texts.plural);
  const nothing = query.trim() ? t.common.noMatch(query.trim()) : t.picker.none(texts.plural);
  const count = options.length === 1 ? `1 ${texts.singular}` : `${options.length} ${texts.plural}`;
  const announcement = options.length === 0 ? nothing : (note ?? count);

  const openList = () => {
    if (open) return;
    const initial = search(query);
    setActive([...initial.suggested, ...initial.matches].findIndex(item => item.id === value));
    setOpen(true);
  };
  const close = () => {
    setOpen(false);
    setDraft(null);
  };
  const choose = (item: PickerItem) => {
    if (item.blocked) return;
    close();
    onChange(item.id);
  };
  const clear = () => {
    close();
    onChange(null);
    input.current?.focus();
  };

  const reposition = () => {
    if (!input.current) return;
    if (isScrolledAway(input.current)) {
      setPlace(null);
      return;
    }
    const next = placeList(input.current.getBoundingClientRect(), { width: innerWidth, height: innerHeight });
    setPlace(previous => (samePlacement(previous, next) ? previous : next));
  };
  // On every render: another field of the form can grow above this one while the list is open.
  useLayoutEffect(() => {
    if (open) reposition();
  });
  useEffect(() => {
    if (!open) return;
    addEventListener('scroll', reposition, true);
    addEventListener('resize', reposition);
    return () => {
      removeEventListener('scroll', reposition, true);
      removeEventListener('resize', reposition);
    };
  }, [open]);

  // A Radix dialog listens for Escape on `document` in the capture phase: only a capture listener on `window` runs before it.
  useEffect(() => {
    if (!open) return;
    const onEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key !== 'Escape' || event.isComposing) return;
      event.preventDefault();
      event.stopPropagation();
      close();
    };
    addEventListener('keydown', onEscape, true);
    return () => removeEventListener('keydown', onEscape, true);
  }, [open]);

  useEffect(() => {
    if (open && current >= 0) document.getElementById(optionId(current))?.scrollIntoView({ block: 'nearest' });
  }, [open, current, query]); // eslint-disable-line react-hooks/exhaustive-deps

  const onKey = (event: KeyboardEvent) => {
    // Never submits, not even with the list closed: an Enter held a moment too long after choosing would create the booking.
    if (event.key === 'Enter') event.preventDefault();
    if (event.nativeEvent.isComposing || event.shiftKey || event.ctrlKey || event.metaKey) return;
    if (!open) {
      const opens = event.key === 'ArrowDown' || event.key === 'ArrowUp' || (event.key === 'Enter' && !event.repeat);
      if (!opens) return;
      event.preventDefault();
      openList();
      return;
    }
    if (event.key === 'Enter') {
      if (!event.repeat && options[current]) choose(options[current]);
      return;
    }
    const next = optionAfterKey(event.key, current, options.length);
    if (next === null) return;
    event.preventDefault();
    setActive(next);
  };

  const renderOption = (item: PickerItem, index: number) => {
    const isCurrent = index === current;
    const isChosen = item.id === value;
    const labelColour = item.blocked ? 'text-ink-2' : isCurrent ? 'text-blue-ink' : 'text-ink';
    return (
      <div
        key={item.id}
        id={optionId(index)}
        role="option"
        aria-selected={isCurrent}
        aria-disabled={item.blocked || undefined}
        onMouseMove={() => {
          if (!isCurrent) setActive(index);
        }}
        onClick={() => choose(item)}
        className={`flex items-center gap-2 rounded-md px-3 py-1.5 ${item.blocked ? 'cursor-not-allowed' : 'cursor-pointer'} ${isCurrent ? (item.blocked ? 'bg-muted' : 'bg-blue-soft') : ''}`}
      >
        <span className="min-w-0 flex-1">
          <span className={`block text-data font-medium ${labelColour}`}>{item.label}</span>
          <span className="block text-xs text-ink-2">
            {item.tags?.map(tag => <Tag key={tag}>{tag}</Tag>)}
            {item.detail}
          </span>
        </span>
        {isChosen && (
          <>
            <Check className="h-4 w-4 shrink-0 text-blue-ink" aria-hidden />
            <span className="sr-only">{t.picker.chosen}</span>
          </>
        )}
      </div>
    );
  };
  const choosable = found.suggested.length + found.matches.length;

  return (
    <div className="min-w-0">
      <label className="label" htmlFor={id}>
        {label}
      </label>
      <div className="relative mt-1">
        <input
          ref={input}
          id={id}
          type="text"
          role="combobox"
          aria-expanded={open}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={open && current >= 0 ? optionId(current) : undefined}
          aria-describedby={showDetail ? detailId : undefined}
          autoComplete="off"
          autoFocus={autoFocus}
          spellCheck={false}
          placeholder={texts.placeholder}
          className={`input w-full truncate pr-9 ${inputClass}`}
          value={draft ?? chosenName}
          required={required}
          onChange={event => {
            setDraft(event.target.value);
            setActive(event.target.value.trim() ? 0 : -1);
            setOpen(true);
          }}
          onClick={() => {
            if (open) return;
            openList();
            input.current?.select(); // Typing replaces the chosen name.
          }}
          onKeyDown={onKey}
          onBlur={() => {
            if (draft === '' && clearable && value !== null) onChange(null);
            close();
          }}
        />
        {clearable && value !== null ? (
          <button
            type="button"
            tabIndex={-1}
            aria-label={t.picker.removeOf(label.toLowerCase())}
            className="absolute inset-y-0 right-0 grid w-9 place-items-center text-ink-3 hover:text-ink"
            onMouseDown={event => event.preventDefault()}
            onClick={clear}
          >
            <X className="h-4 w-4" aria-hidden />
          </button>
        ) : (
          <ChevronDown className="pointer-events-none absolute right-2.5 top-2.5 h-4 w-4 text-ink-3" aria-hidden />
        )}
      </div>
      {showDetail && (
        <p id={detailId} className="mt-0.5 min-h-[16px] text-xs text-ink-2">
          {chosen?.tags?.map(tag => <Tag key={tag}>{tag}</Tag>)}
          {chosen?.detail}
        </p>
      )}
      <span role="status" className="sr-only">
        {open && announcement}
      </span>
      {open && (
        <div
          ref={showInTopLayer}
          {...{ popover: 'manual' }}
          className="z-50 m-0 flex flex-col overflow-hidden rounded-md bg-surface p-0 text-ink shadow-pop"
          style={{
            position: 'fixed',
            left: place?.left,
            right: 'auto',
            top: place?.top ?? 'auto',
            bottom: place?.bottom ?? 'auto',
            width: place?.width,
            maxHeight: place?.maxHeight,
            visibility: place ? undefined : 'hidden',
          }}
          onMouseDown={event => event.preventDefault()} // The field keeps the focus: losing it closes the list before the click.
        >
          {/* tabIndex: a scrollable area is a tab stop in Chrome, and this one disappears when the field loses the focus. */}
          <div
            id={listId}
            role="listbox"
            aria-label={label}
            tabIndex={-1}
            className="min-h-0 flex-1 overflow-y-auto overscroll-contain p-1"
          >
            {found.suggested.length > 0 && suggested && (
              <OptionGroup id={`${id}-suggested`} title={suggested.label}>
                {found.suggested.map(renderOption)}
              </OptionGroup>
            )}
            {found.matches.length > 0 && (
              <OptionGroup id={`${id}-matches`} title={found.suggested.length > 0 && suggested ? suggested.others : null}>
                {found.matches.map((item, index) => renderOption(item, found.suggested.length + index))}
              </OptionGroup>
            )}
            {found.blocked.length > 0 && (
              <OptionGroup id={`${id}-blocked`} title={t.picker.notChoosable}>
                {found.blocked.map((item, index) => renderOption(item, choosable + index))}
              </OptionGroup>
            )}
          </div>
          {options.length === 0 && <p className="px-3 py-6 text-center text-meta text-ink-2">{nothing}</p>}
          {note && <p className="border-t border-line bg-sunken px-3 py-1.5 text-xs text-ink-2">{note}</p>}
        </div>
      )}
    </div>
  );
}

const OptionGroup = ({ id, title, children }: { id: string; title: string | null; children: ReactNode }) =>
  title === null ? (
    <>{children}</>
  ) : (
    <div role="group" aria-labelledby={id}>
      <p id={id} className="px-3 pb-1 pt-2 text-xs font-medium text-ink-3">
        {title}
      </p>
      {children}
    </div>
  );
