import { EmptyState, LargeTitleHeader, Screen } from '../../ui';

/** Fase 0: lege plek; inhoud volgt in fase 6 (publieke app). */
export default function ProgrammaScreen() {
  return (
    <Screen>
      <LargeTitleHeader title="Programma" />
      <EmptyState icon="programma" title="Binnenkort beschikbaar" message="Hier verschijnt straks het programma van het carnavalsseizoen." />
    </Screen>
  );
}
