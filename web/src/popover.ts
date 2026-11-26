// Popovers painted in a portal: fixed to the window, so no ancestor with overflow or z-index clips them.

export interface Anchor {
  left: number;
  top: number;
  right: number;
  bottom: number;
  width: number;
  height: number;
}
export interface Placement {
  left: number;
  top: number;
  side: 'top' | 'bottom';
}

const GAP = 8,
  MARGIN = 8;

/** Above the anchor; below if it does not fit; if neither fits, where there is more room. Never leaves the window horizontally. */
export function placePopover(
  anchor: Anchor,
  size: { width: number; height: number },
  viewport: { width: number; height: number },
): Placement {
  const fitsTop = anchor.top - GAP - size.height >= MARGIN;
  const fitsBottom = anchor.bottom + GAP + size.height <= viewport.height - MARGIN;
  const side = fitsTop || (!fitsBottom && anchor.top > viewport.height - anchor.bottom) ? 'top' : 'bottom';
  const top = side === 'top' ? Math.max(MARGIN, anchor.top - GAP - size.height) : anchor.bottom + GAP;
  const centered = anchor.left + anchor.width / 2 - size.width / 2;
  const left = Math.min(Math.max(MARGIN, centered), Math.max(MARGIN, viewport.width - MARGIN - size.width));
  return { left, top, side };
}

export interface ListPlacement {
  left: number;
  width: number;
  maxHeight: number;
  /** Distance to the window edge the list hangs from: `top` when it opens downwards, `bottom` when it opens upwards. */
  top?: number;
  bottom?: number;
}

const LIST_GAP = 4,
  LIST_HEIGHT = 340,
  LIST_MIN_WIDTH = 320;

/** Below the field; above only if it does not fit below and there is more room above. Shrinks before leaving the window. */
export function placeList(anchor: Anchor, viewport: { width: number; height: number }): ListPlacement {
  const roomBelow = viewport.height - MARGIN - anchor.bottom - LIST_GAP;
  const roomAbove = anchor.top - MARGIN - LIST_GAP;
  const width = Math.min(Math.max(anchor.width, LIST_MIN_WIDTH), viewport.width - 2 * MARGIN);
  const left = Math.max(MARGIN, Math.min(anchor.left, viewport.width - MARGIN - width));
  const upwards = roomBelow < LIST_HEIGHT && roomAbove > roomBelow;
  const maxHeight = Math.max(0, Math.min(LIST_HEIGHT, upwards ? roomAbove : roomBelow));
  return upwards
    ? { left, width, maxHeight, bottom: viewport.height - anchor.top + LIST_GAP }
    : { left, width, maxHeight, top: anchor.bottom + LIST_GAP };
}
