import { iconPaths, type IconName } from './icons';

export type { IconName } from './icons';

/**
 * Lijnicoon (24 px, stroke 2) in de kleur van de tekst eromheen. Decoratief tenzij er een label is; knoppen met
 * alleen een icoon krijgen hun naam via <code>aria-label</code> op de knop.
 */
export function Icon({ name, size = 20, label }: { name: IconName; size?: number; label?: string }) {
  const icon = iconPaths[name];
  return (
    <svg
      className="icon"
      width={size}
      height={size}
      viewBox={icon.viewBox}
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      focusable="false"
      // Vaste, eigen SVG-paden uit het ontwerp (icons.ts); geen invoer van gebruikers.
      dangerouslySetInnerHTML={{ __html: icon.body }}
    />
  );
}
