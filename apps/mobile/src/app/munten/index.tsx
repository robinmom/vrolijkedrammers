import { router } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { useSessionStatus } from '../../auth/useSession';
import { TicketScreen } from '../../features/TicketScreen';
import { tokenTickets, useMyOrders } from '../../features/sales';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, LargeTitleHeader, QueryState, Screen } from '../../ui';

/**
 * Munten (fase 19b, tegel op het beginscherm, Figma "iOS / 8 Munten-QR"). Zijn er betaalde munten af te halen, dan per
 * bestelling een munten-QR (swipen naar de volgende): gekoppeld aan dit toestel, elke 30 seconden nieuw en niet te delen.
 * Anders munten kopen.
 */
export default function MuntenScreen() {
  const { colors } = useTheme();
  const status = useSessionStatus();
  const orders = useMyOrders();
  const tickets = tokenTickets(orders.data);
  const buy = <Button label="Munten kopen" variant="secondary" onPress={() => router.push('/munten/kopen')} />;

  if (status === 'signedIn' && tickets.length > 0) {
    return <TicketScreen purpose="Tokens" tokenTickets={tickets} before={<View style={styles.before}>{buy}</View>} />;
  }

  return (
    <Screen>
      <BackLink label="Home" />
      <LargeTitleHeader title="Munten" />
      <View style={styles.content}>
        {status === 'signedIn' && !orders.data ? (
          <QueryState query={orders} />
        ) : (
          <Card style={styles.card}>
            <AppText variant="cardTitle">
              {status === 'signedIn' ? 'Geen munten af te halen' : 'Munten zijn voor leden'}
            </AppText>
            <AppText variant="body" color={colors.textSecondary}>
              Koop munten vooraf en haal ze op bij de kassa: daar wordt je munten-QR gescand en krijg je de munten.
              Aankoop is persoonsgebonden.
            </AppText>
            {status === 'signedIn' ? buy : <Button label="Inloggen" onPress={() => router.push('/meer/inloggen')} />}
          </Card>
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 14, paddingBottom: 24 },
  before: { paddingHorizontal: 20, paddingBottom: 8 },
  card: { padding: 16, gap: 12 },
});
