import { EmptyState } from '../ui';
import { InfoPage } from '../features/InfoPage';

/** Meldingen: de inbox en pushmeldingen volgen in fase 10. */
export default function MeldingenScreen() {
  return (
    <InfoPage title="Meldingen" backLabel="Terug">
      <EmptyState icon="meldingen" title="Nog geen meldingen" message="Binnenkort ontvang je hier berichten van de vereniging, bijvoorbeeld over wijzigingen in het programma." />
    </InfoPage>
  );
}
