import { t } from './i18n/t';
import { locationItems } from './locationItems';
import { SearchPicker, type PickerTexts } from './SearchPicker';
import type { Location } from './types';

const TEXTS: PickerTexts = { get placeholder() { return t.place.search; }, singular: 'sitio', plural: 'sitios' };

/** Offered first when nothing has been typed yet. */
export interface SuggestedLocations {
  label: string;
  ids: number[];
}

/** `value` is the Id as text ('' = none), as the forms keep it. */
export function LocationPicker({
  label,
  value,
  locations,
  onChange,
  suggested,
  required,
}: {
  label: string;
  value: string;
  locations: Location[];
  onChange: (id: string) => void;
  suggested?: SuggestedLocations;
  required?: boolean;
}) {
  return (
    <SearchPicker
      label={label}
      value={value === '' ? null : Number(value)}
      items={locationItems(locations)}
      texts={TEXTS}
      onChange={id => onChange(id === null ? '' : String(id))}
      suggested={suggested && { ...suggested, others: t.place.others }}
      required={required}
    />
  );
}
