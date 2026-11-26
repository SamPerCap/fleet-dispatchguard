import { describe, expect, it } from 'vitest';
import { locationDetail, locationItems } from './locationItems';
import { MAX_OPTIONS, optionAfterKey, optionsNote, searchItems, type PickerItem } from './pickerSearch';
import type { Location } from './types';

let nextId = 1;
const site = (Name: string, Address = 'Calle Uno', City = 'Madrid', Province = 'Madrid'): Location => ({
  Id: nextId++,
  Name,
  Address,
  City,
  Province,
});
const searchLocations = (locations: Location[], query: string, used?: number[]) => {
  const { suggested, matches, total } = searchItems(locationItems(locations), query, used);
  const ids = (items: PickerItem[]) => items.map(item => locations.find(l => l.Id === item.id)!);
  return { suggested: ids(suggested), matches: ids(matches), total };
};
const names = (locations: Location[], query: string) => searchLocations(locations, query).matches.map(l => l.Name);

describe('searching locations', () => {
  it('searchLocations_MatchesNameAddressCityAndProvince', () => {
    const locations = [
      site('Intercambiador'),
      site('Estación', 'Avenida del Puerto 3', 'Sagunt', 'Valencia'),
      site('Hotel', 'Calle Dos', 'Azuqueca', 'Guadalajara'),
    ];
    expect(names(locations, 'interc')).toEqual(['Intercambiador']);
    expect(names(locations, 'puerto')).toEqual(['Estación']);
    expect(names(locations, 'sagunt')).toEqual(['Estación']);
    expect(names(locations, 'guadalajara')).toEqual(['Hotel']);
  });

  it('searchLocations_IgnoresAccentsAndCase', () => {
    const locations = [site('Estación de Ávila'), site('PEÑÍSCOLA')];
    expect(names(locations, 'ESTACION avila')).toEqual(['Estación de Ávila']);
    expect(names(locations, 'peñiscola')).toEqual(['PEÑÍSCOLA']);
    expect(names(locations, 'peniscola')).toEqual(['PEÑÍSCOLA']);
  });

  it('searchLocations_SeveralWordsInAnyOrder_AllMustAppear', () => {
    const locations = [site('Puerta del Sol', 'Plaza Mayor', 'Madrid'), site('Puerta Real', 'Calle Sol', 'Granada', 'Granada')];
    expect(names(locations, 'sol puerta madrid')).toEqual(['Puerta del Sol']);
    expect(names(locations, 'puerta sol')).toEqual(['Puerta del Sol', 'Puerta Real']);
    expect(names(locations, 'puerta bilbao')).toEqual([]);
  });

  it('searchLocations_PunctuationIsASeparator', () => {
    const locations = [site('Barajas (T4)', "C/ L'Estació, 5")];
    expect(names(locations, 't4')).toEqual(['Barajas (T4)']);
    expect(names(locations, 'barajas, l’estació 5')).toEqual(['Barajas (T4)']);
  });

  it('searchLocations_NameStart_ThenWordStart_ThenContained', () => {
    const locations = [site('Girasoles'), site('Puerta del Sol'), site('Casa', 'Calle Sol'), site('Sol - Metro')];
    expect(names(locations, 'sol')).toEqual(['Sol - Metro', 'Puerta del Sol', 'Casa', 'Girasoles']);
  });

  it('searchLocations_RankDoesNotDependOnWordOrder', () => {
    const locations = [site('Andén', 'Calle Estación'), site('Estación Sur')];
    expect(names(locations, 'estacion madrid')).toEqual(['Estación Sur', 'Andén']);
    expect(names(locations, 'madrid estacion')).toEqual(['Estación Sur', 'Andén']);
  });

  it('searchLocations_SameRank_AlphabeticalInSpanish', () => {
    const locations = [site('Parada Zoco'), site('Parada Ñandú'), site('Parada 10'), site('Parada Ávila'), site('Parada Norte'), site('Parada 2')];
    expect(names(locations, 'parada')).toEqual(['Parada 2', 'Parada 10', 'Parada Ávila', 'Parada Norte', 'Parada Ñandú', 'Parada Zoco']);
  });

  it('searchLocations_Homonyms_StableOrderByDetail', () => {
    const inMadrid = site('Estación', 'Calle B', 'Madrid');
    const inToledo = site('Estación', 'Calle A', 'Toledo', 'Toledo');
    expect(searchLocations([inMadrid, inToledo], 'estacion').matches).toEqual([inToledo, inMadrid]);
    expect(searchLocations([inToledo, inMadrid], 'estacion').matches).toEqual([inToledo, inMadrid]);
  });

  it('searchLocations_MoreThanTwoThousand_NeverMoreThanTheCap', () => {
    const locations = Array.from({ length: 2500 }, (_, index) => site(`Parada ${index}`, `Calle ${index}`, index % 2 ? 'Madrid' : 'Getafe'));
    const count = (query: string, used: number[] = []) => {
      const found = searchLocations(locations, query, used);
      return { shown: found.suggested.length + found.matches.length, total: found.total };
    };
    expect(count('parada')).toEqual({ shown: MAX_OPTIONS, total: 2500 });
    expect(count('getafe')).toEqual({ shown: MAX_OPTIONS, total: 1250 });
    expect(count('')).toEqual({ shown: MAX_OPTIONS, total: 2500 });
    expect(count('', locations.slice(0, 5).map(l => l.Id))).toEqual({ shown: MAX_OPTIONS, total: 2500 });
    expect(count('', locations.map(l => l.Id))).toEqual({ shown: MAX_OPTIONS, total: 2500 });
    expect(count('parada 2499')).toEqual({ shown: 1, total: 1 });
  });

  it('searchLocations_EmptyQuery_UsedInTheBookingFirst_NotRepeated', () => {
    const [alpha, beta, gamma] = [site('Alfa'), site('Beta'), site('Gamma')];
    const found = searchLocations([gamma, beta, alpha], '  ', [gamma.Id, beta.Id, gamma.Id, 99999]);
    expect(found).toEqual({ suggested: [gamma, beta], matches: [alpha], total: 3 });
  });

  it('searchLocations_WithQuery_IgnoresTheUsedOnes', () => {
    const [alpha, beta] = [site('Alfa'), site('Beta')];
    expect(searchLocations([alpha, beta], 'alf', [beta.Id])).toEqual({ suggested: [], matches: [alpha], total: 1 });
  });

  it('searchLocations_SameListSearchedAgain_TextIsNotNormalizedAgain', () => {
    let reads = 0;
    const counted = Object.defineProperty(site('x'), 'Address', { get: () => (reads++, 'Calle Mayor') }) as Location;
    const locations = [counted, site('Otra')];
    searchLocations(locations, 'calle');
    const afterFirst = reads;
    searchLocations(locations, 'mayor');
    searchLocations(locations, '');
    expect(reads).toBe(afterFirst);

    let keywordReads = 0;
    const items = [Object.defineProperty({ id: 1, label: 'x', detail: '' }, 'keywords', { get: () => (keywordReads++, 'y') })];
    searchItems(items, 'x');
    searchItems(items, 'y');
    expect(keywordReads).toBe(1);
  });

  it('searchLocations_NewList_IsSearchedAfresh', () => {
    const before = [site('Antigua')];
    expect(names(before, 'nueva')).toEqual([]);
    expect(names([...before, site('Nueva')], 'nueva')).toEqual(['Nueva']);
  });
});

describe('locationDetail', () => {
  it('locationDetail_AddressAndCity', () => {
    expect(locationDetail(site('Sol', 'Plaza 1', 'Madrid'))).toBe('Plaza 1 · Madrid');
    expect(locationDetail(site('Sol', '', 'Madrid'))).toBe('Madrid');
  });
});

describe('searchItems', () => {
  const item = (id: number, label: string, rest: Partial<PickerItem> = {}): PickerItem => ({ id, label, detail: '', ...rest });
  const ids = (items: PickerItem[]) => items.map(i => i.id);

  it('searchItems_EqualMatch_KeepsTheGivenOrder', () => {
    const items = [item(3, 'Zoe Ana'), item(1, 'Ana Zeta'), item(2, 'Ana Alfa')];
    expect(ids(searchItems(items, 'ana').matches)).toEqual([1, 2, 3]);
    expect(ids(searchItems(items, '').matches)).toEqual([3, 1, 2]);
  });

  it('searchItems_Blocked_OnlyWhenSearched_AndLast', () => {
    const items = [item(1, 'Ana', { blocked: true }), item(2, 'Mariana'), item(3, 'Luis')];
    expect(searchItems(items, '')).toEqual({ suggested: [], matches: [items[1], items[2]], blocked: [], total: 2 });
    expect(searchItems(items, 'ana')).toEqual({ suggested: [], matches: [items[1]], blocked: [items[0]], total: 2 });
    expect(searchItems(items, '', [1]).suggested).toEqual([]);
  });

  it('searchItems_Blocked_TakeOnlyTheRoomLeft', () => {
    const many = (count: number, from: number, blocked: boolean) =>
      Array.from({ length: count }, (_, index) => item(from + index, `Ana ${index}`, { blocked }));
    const full = searchItems([...many(30, 1, false), ...many(30, 100, true)], 'ana');
    expect([full.matches.length, full.blocked.length, full.total]).toEqual([MAX_OPTIONS, 0, 60]);
    const some = searchItems([...many(15, 1, false), ...many(30, 100, true)], 'ana');
    expect([some.matches.length, some.blocked.length, some.total]).toEqual([15, MAX_OPTIONS - 15, 45]);
  });

  it('searchItems_DetailIsNotSearched', () => {
    expect(searchItems([item(1, 'Ana', { detail: 'descanso semanal' })], 'semanal').total).toBe(0);
  });
});

describe('optionsNote', () => {
  it('optionsNote_OnlyWhenSomethingIsHidden', () => {
    expect(optionsNote(20, 20, 'sol', 'sitios')).toBeNull();
    expect(optionsNote(20, 21, 'sol', 'sitios')).toBe('20 de 21 sitios: sigue escribiendo para acotar');
    expect(optionsNote(20, 143, '', 'conductores')).toBe('20 de 143 conductores: escribe para acotar');
  });
});

describe('optionAfterKey', () => {
  it('optionAfterKey_NothingActive_DownIsFirstAndUpIsLast', () => {
    expect(optionAfterKey('ArrowDown', -1, 5)).toBe(0);
    expect(optionAfterKey('ArrowUp', -1, 5)).toBe(4);
  });

  it('optionAfterKey_StopsAtBothEnds', () => {
    expect(optionAfterKey('ArrowDown', 4, 5)).toBe(4);
    expect(optionAfterKey('ArrowUp', 0, 5)).toBe(0);
    expect(optionAfterKey('ArrowUp', 3, 5)).toBe(2);
    expect(optionAfterKey('Home', 3, 5)).toBe(0);
    expect(optionAfterKey('End', 0, 5)).toBe(4);
  });

  it('optionAfterKey_NoOptionsOrAnotherKey_Null', () => {
    expect(optionAfterKey('ArrowDown', -1, 0)).toBeNull();
    expect(optionAfterKey('a', 0, 5)).toBeNull();
  });
});
