import type { ColorScheme } from './colors';

/** Zet een kleurenschema om naar CSS custom properties voor het beheerportal (ADR-013). */
export function toCssVariables(scheme: ColorScheme): string {
  return Object.entries(scheme)
    .map(([name, value]) => `--dvd-${name.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`)}: ${value};`)
    .join('\n');
}
