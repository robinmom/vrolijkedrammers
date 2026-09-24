import { EmptyState, LargeTitleHeader, Screen } from '../../ui';

/** Fase 0: lege plek; inhoud volgt in fase 6 (publieke app). */
export default function HomeScreen() {
  return (
    <Screen>
      <LargeTitleHeader title="De Vrolijke Drammers" />
      <EmptyState icon="home" title="Binnenkort beschikbaar" message="Hier verschijnen straks de eerstvolgende activiteit, snelkoppelingen en het laatste nieuws." />
    </Screen>
  );
}
