import { SvgXml } from 'react-native-svg';
import { icons, type IconName } from './icons.generated';

export type { IconName } from './icons.generated';

/**
 * Kleurt een Figma-icoon in door alle stroke-kleuren te vervangen; de bronbestanden blijven ongewijzigd.
 * Zonder `color` wordt het icoon in de oorspronkelijke Figma-kleur getoond.
 */
export function tintSvg(svg: string, color: string): string {
  return svg.replace(/stroke="[^"]*"/g, `stroke="${color}"`).replace(/\sstroke-opacity="[^"]*"/g, '');
}

interface IconProps {
  name: IconName;
  size?: number;
  color?: string;
  /** Alleen invullen als het icoon zelfstandig betekenis heeft; anders is het decoratief. */
  accessibilityLabel?: string;
}

export function Icon({ name, size = 24, color, accessibilityLabel }: IconProps) {
  const xml = color ? tintSvg(icons[name], color) : icons[name];
  return (
    <SvgXml
      xml={xml}
      width={size}
      height={size}
      accessible={Boolean(accessibilityLabel)}
      accessibilityLabel={accessibilityLabel}
      accessibilityElementsHidden={!accessibilityLabel}
      importantForAccessibility={accessibilityLabel ? 'yes' : 'no-hide-descendants'}
    />
  );
}
