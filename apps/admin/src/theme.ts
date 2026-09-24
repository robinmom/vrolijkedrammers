import { dark, light, toCssVariables } from '@drammers/design-tokens';

/**
 * Levert de design tokens als CSS custom properties, met automatische licht/donker-wissel
 * op basis van de systeeminstelling (ADR-013).
 */
export function themeStylesheet(): string {
  return `:root {\n${toCssVariables(light)}\n}\n@media (prefers-color-scheme: dark) {\n  :root {\n${toCssVariables(dark)}\n  }\n}`;
}

export function applyTheme(doc: Document = document): void {
  const style = doc.createElement('style');
  style.dataset.dvdTheme = 'true';
  style.textContent = themeStylesheet();
  doc.head.appendChild(style);
}
