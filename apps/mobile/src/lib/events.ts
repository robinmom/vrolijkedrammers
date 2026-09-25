import type { EventSummary } from '../api/client';
import type { BadgeVariant } from '../ui/Badge';

/**
 * Label naast de titel (Figma 02): een hoogtepunt krijgt geel ("Hoogtepunt" of de eigen badgetekst),
 * een andere badgetekst en de categorie Jeugd krijgen groen.
 */
export function eventBadge(event: Pick<EventSummary, 'isHighlight' | 'badgeText' | 'category'>): { label: string; variant: BadgeVariant } | undefined {
  if (event.isHighlight) {
    return { label: event.badgeText ?? 'Hoogtepunt', variant: 'highlight' };
  }
  if (event.badgeText) {
    return { label: event.badgeText, variant: 'youth' };
  }
  return event.category.code === 'jeugd' ? { label: event.category.name, variant: 'youth' } : undefined;
}
