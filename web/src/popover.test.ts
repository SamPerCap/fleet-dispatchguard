import { describe, expect, it } from 'vitest';
import { placeList, placePopover } from './popover';

const anchor = (left: number, top: number, width = 16, height = 16) => ({
  left,
  top,
  right: left + width,
  bottom: top + height,
  width,
  height,
});
const size = { width: 288, height: 100 };
const viewport = { width: 1440, height: 900 };

describe('placePopover (ⓘ in a portal)', () => {
  it('above and centred when it fits', () => {
    expect(placePopover(anchor(700, 400), size, viewport)).toEqual({ side: 'top', top: 400 - 8 - 100, left: 708 - 144 });
  });
  it('below if it does not fit above (ⓘ next to the top edge)', () => {
    expect(placePopover(anchor(700, 40), size, viewport)).toMatchObject({ side: 'bottom', top: 56 + 8 });
  });
  it('does not leave the window on the left or on the right', () => {
    expect(placePopover(anchor(2, 400), size, viewport).left).toBe(8);
    expect(placePopover(anchor(1430, 400), size, viewport).left).toBe(1440 - 8 - 288);
  });
  it('no room above or below: where there is more, without leaving through the top', () => {
    const tiny = { width: 400, height: 150 };
    expect(placePopover(anchor(100, 60), size, tiny).side).toBe('bottom');
    const placement = placePopover(anchor(100, 100), size, tiny);
    expect(placement.side).toBe('top');
    expect(placement.top).toBeGreaterThanOrEqual(8);
  });
});

describe('placeList', () => {
  const viewport = { width: 1000, height: 800 };
  const field = (left: number, top: number, width = 200) => ({ left, top, width, height: 36, right: left + width, bottom: top + 36 });

  it('placeList_RoomBelow_HangsFromTheFieldAtFullHeight', () => {
    expect(placeList(field(100, 100), viewport)).toEqual({ left: 100, width: 320, maxHeight: 340, top: 140 });
  });

  it('placeList_WideField_SameWidthAsTheField', () => {
    expect(placeList(field(100, 100, 500), viewport).width).toBe(500);
  });

  it('placeList_NoRoomBelow_OpensUpwards', () => {
    expect(placeList(field(100, 700), viewport)).toEqual({ left: 100, width: 320, maxHeight: 340, bottom: 104 });
  });

  it('placeList_FitsNeither_LargerSideAndShrinks', () => {
    const short = { width: 1000, height: 400 };
    expect(placeList(field(100, 100), short)).toMatchObject({ top: 140, maxHeight: 252 });
    expect(placeList(field(100, 260), short)).toMatchObject({ bottom: 144, maxHeight: 248 });
  });

  it('placeList_NeverLeavesTheWindowSideways', () => {
    expect(placeList(field(900, 100, 90), viewport).left).toBe(672);
    expect(placeList(field(0, 100), { width: 300, height: 800 })).toMatchObject({ left: 8, width: 284 });
  });
});
