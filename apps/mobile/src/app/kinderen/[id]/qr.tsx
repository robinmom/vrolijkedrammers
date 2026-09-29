import { useLocalSearchParams } from 'expo-router';
import { TicketScreen } from '../../../features/TicketScreen';
import { isGuid } from '../../../lib/ids';
import { BackLink, EmptyState, Screen } from '../../../ui';

/** QR van een kind op de telefoon van de ouder (fase 17, Figma scherm 3); zelfde regels als Mijn QR. */
export default function KindQrScreen() {
  const { id, naam } = useLocalSearchParams<{ id: string; naam?: string }>();
  if (!isGuid(id)) {
    return (
      <Screen>
        <BackLink label="Mijn kinderen" />
        <EmptyState title="Kind niet gevonden" message="Dit kind is niet (meer) aan je account gekoppeld." />
      </Screen>
    );
  }
  return <TicketScreen childId={id} childName={typeof naam === 'string' && naam ? naam.slice(0, 40) : undefined} />;
}
