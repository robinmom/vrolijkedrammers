import { EmptyState, LargeTitleHeader, Screen } from '../../ui';

/** Fase 0: lege plek; inhoud volgt in fase 6 (publieke app). */
export default function NieuwsScreen() {
  return (
    <Screen>
      <LargeTitleHeader title="Nieuws" />
      <EmptyState icon="nieuws" title="Binnenkort beschikbaar" message="Hier verschijnt straks het laatste nieuws van de vereniging." />
    </Screen>
  );
}
