import { EmptyState, LargeTitleHeader, Screen } from '../../ui';

/** Fase 0: lege plek; inhoud volgt in fase 6 (publieke app). */
export default function OptochtScreen() {
  return (
    <Screen>
      <LargeTitleHeader title="Optocht" />
      <EmptyState icon="optocht" title="Binnenkort beschikbaar" message="Hier verschijnt straks alle informatie over de optocht." />
    </Screen>
  );
}
