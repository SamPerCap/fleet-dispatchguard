import type { PickerItem } from './pickerSearch';
import type { Location } from './types';

const byName = new Intl.Collator('es', { numeric: true });

export const locationDetail = (location: Location) => [location.Address, location.City].filter(Boolean).join(' · ');

function buildItems(locations: Location[]): PickerItem[] {
  return locations
    .map(location => ({
      id: location.Id,
      label: location.Name,
      detail: locationDetail(location),
      keywords: `${location.Address} ${location.City} ${location.Province}`,
    }))
    .sort((a, b) => byName.compare(a.label, b.label) || byName.compare(a.detail, b.detail) || a.id - b.id);
}

// The list is loaded once and every field uses it, so it is sorted only once.
const itemsByList = new WeakMap<Location[], PickerItem[]>();

/** Alphabetical. */
export function locationItems(locations: Location[]): PickerItem[] {
  let items = itemsByList.get(locations);
  if (!items) {
    items = buildItems(locations);
    itemsByList.set(locations, items);
  }
  return items;
}
