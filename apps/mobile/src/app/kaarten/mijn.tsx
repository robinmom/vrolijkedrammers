import { router } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { useRefresh } from '../../api/useRefresh';
import { useSessionStatus } from '../../auth/useSession';
import { OrderCard } from '../../features/SaleParts';
import { salesKeys, useGuestOrders, useMyOrders, type OrderView } from '../../features/sales';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, EmptyState, LargeTitleHeader, QueryState, Screen } from '../../ui';

/**
 * Mijn kaarten (fase 19b, Figma "iOS / 5"): één QR per bestelling voor alle kaarten, gedeelde kaarten van een
 * groepslid, en bestellingen die op dit toestel zijn gedaan zonder account.
 */
export default function MijnKaartenScreen() {
  const { colors } = useTheme();
  const status = useSessionStatus();
  const mine = useMyOrders();
  const guest = useGuestOrders();
  const refresh = useRefresh([salesKeys.myOrders, salesKeys.guestOrders]);
  const orders: OrderView[] = [...(mine.data ?? [])];
  for (const o of guest.data ?? []) {
    if (!orders.some((x) => x.id === o.id)) orders.push(o);
  }
  // Munten staan onder de tegel Munten; hier alleen kaarten.
  const tickets = orders.filter((o) => o.kind !== 'Tokens');
  const loading = (status === 'signedIn' && !mine.data && !mine.isError) || (!guest.data && !guest.isError);

  return (
    <Screen {...refresh}>
      <BackLink label="Kaarten" />
      <LargeTitleHeader title="Mijn kaarten" />
      <View style={styles.content}>
        {loading ? (
          <QueryState query={status === 'signedIn' ? mine : guest} />
        ) : tickets.length === 0 ? (
          <EmptyState title="Nog geen kaarten" message="Kaarten die je koopt, staan hier met de QR voor de ingang." />
        ) : (
          <>
            <AppText variant="body" color={colors.textSecondary}>
              Eén QR voor de hele bestelling. Bij het scannen gaan alle personen tegelijk naar binnen en wordt de QR
              geblokkeerd. Deel je een kaart met een lid van je groep, dan krijgt die een eigen QR.
            </AppText>
            {tickets.map((o) => (
              <OrderCard key={`${o.id}-${o.sharedBy ?? ''}`} order={o} />
            ))}
          </>
        )}
        <Button label="Kaarten kopen" variant="secondary" onPress={() => router.push('/kaarten')} />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16, paddingBottom: 24 },
});
