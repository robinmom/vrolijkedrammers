import { useWindowDimensions } from 'react-native';

/**
 * Breedte waarboven we de tabletindeling gebruiken. We kijken naar de beschikbare breedte en niet naar
 * "is dit een tablet": een vouwbare telefoon kan opengevouwen breed zijn en in Split View juist smal, en een
 * tablet is in portret smal en in landschap breed. Zie de runbook tablet.
 */
export const WIDE_BREAKPOINT = 700;

/** Maximale breedte van een leeskolom; langere tekstregels lezen slecht. Gelijk aan het breekpunt. */
export const MAX_READING_WIDTH = WIDE_BREAKPOINT;

/**
 * Maximale breedte van een scherm dat uit kaarten bestaat in plaats van lopende tekst (agenda, nieuws, het
 * startscherm). De leesbreedte van 700 geldt voor tekstregels; een rij kaarten heeft daar geen last van en staat op
 * een iPad in landschap anders in een smalle band met veel leegte ernaast. Zie runbook tablet.
 */
export const MAX_CONTENT_WIDTH = 1100;

/** True zodra er genoeg breedte is voor de tabletindeling (twee kolommen, grotere aanraakdoelen). */
export function useIsWide() {
  const { width } = useWindowDimensions();
  return width >= WIDE_BREAKPOINT;
}
