/// <reference types="vite/client" />
import { describe, expect, it } from 'vitest';
import tokens from './index.css?raw';

describe('contrast of the booking panel (WCAG 2.1, from the tokens)', () => {
  type Colour = [number, number, number];
  const token = (name: string): Colour => {
    const match = tokens.match(new RegExp(`--${name}:\\s*(\\d+) (\\d+) (\\d+)`));
    if (!match) throw new Error(`Token --${name} is not in index.css`);
    return [Number(match[1]), Number(match[2]), Number(match[3])];
  };
  const over = (front: Colour, alpha: number, back: Colour) => front.map((channel, i) => alpha * channel + (1 - alpha) * back[i]) as Colour;
  const luminance = (colour: Colour) => {
    const [red, green, blue] = colour
      .map(channel => channel / 255)
      .map(channel => (channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4));
    return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
  };
  const contrast = (a: Colour, b: Colour) => {
    const [dark, light] = [luminance(a), luminance(b)].sort((x, y) => x - y);
    return (light + 0.05) / (dark + 0.05);
  };

  it.each([
    ['ink', 'navy-tint', 'title of a trip'],
    ['ink', 'muted', 'title of a cancelled trip, plain notices'],
    ['surface', 'navy', '«Seleccionado»'],
    ['ink', 'surface', 'data'],
    ['ink-2', 'surface', 'labels'],
    ['blue-ink', 'surface', 'arrival on another day'],
    ['critical-ink', 'critical-soft', 'missing driver or vehicle'],
    ['warning-ink', 'warning-soft', 'forced and agenda notices'],
    ['ink', 'sunken', 'header of the booking'],
    ['ink-2', 'sunken', 'description, explanation'],
    ['success-ink', 'sunken', 'all covered'],
    ['critical-ink', 'sunken', 'missing in the coverage line'],
    ['ink-3', 'sunken', 'notes of the hours and the explanation'],
    ['ink', 'blue-soft', 'title of the form'],
    ['ink-2', 'blue-soft', 'texts of the form'],
    ['surface', 'blue', 'main button'],
  ])('text %s on %s (%s) ≥ 4.5:1', (text, back) => {
    expect(contrast(token(text), token(back))).toBeGreaterThanOrEqual(4.5);
  });

  it.each<[string, number, string, string]>([
    ['ink-3', 0.75, 'surface', 'border of a block, buttons and day line'],
    ['ink-3', 0.75, 'blue-soft', 'border of the field and the button of the form'],
    ['ink-3', 0.75, 'warning-soft', 'border of the fields to force'],
    ['ink-3', 1, 'muted', 'edge of a plain notice'],
    ['warning-ink', 1, 'warning-soft', 'edge of a warning notice'],
    ['blue', 1, 'surface', 'ring of the marked block'],
    ['blue', 1, 'blue-soft', 'edge of the form'],
    ['ink-3', 1, 'surface', 'icons'],
  ])('border or icon %s at %s on %s (%s) ≥ 3:1', (front, alpha, back) => {
    expect(contrast(over(token(front), alpha, token(back)), token(back))).toBeGreaterThanOrEqual(3);
  });
});
