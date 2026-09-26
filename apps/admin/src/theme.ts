import { dark, light, toCssVariables } from '@drammers/design-tokens';

/**
 * Levert de design tokens als CSS custom properties, met automatische licht/donker-wissel
 * op basis van de systeeminstelling (ADR-013).
 *
 * Het resultaat staat als bestand in `theme.generated.css` en wordt met de build meegeleverd: de CSP van het portal
 * staat geen inline `<style>` toe. Na een tokenwijziging: `pnpm --filter admin test -u` (de test bewaakt dit).
 */
export function themeStylesheet(): string {
  return `/* Gegenereerd uit @drammers/design-tokens door src/theme.ts – niet handmatig wijzigen. */\n:root {\n${toCssVariables(light)}\n}\n@media (prefers-color-scheme: dark) {\n  :root {\n${toCssVariables(dark)}\n  }\n}\n`;
}
