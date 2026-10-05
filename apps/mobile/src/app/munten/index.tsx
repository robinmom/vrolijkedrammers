import { router } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { useSessionStatus } from '../../auth/useSession';
import { TicketScreen } from '../../features/TicketScreen';
import { tokensElsewhere, tokenTickets, useMyOrders } from '../../features/sales';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, LargeTitleHeader, QueryState, Screen } from '../../ui';

/**
 * Munten (fase 19b, tegel op het beginscherm, Figma "iOS / 8 Munten-QR"). Zijn er betaalde munten af te halen, dan per
 * bestelling een munten-QR (swipen naar de volgende): gekoppeld aan het toestel van de aankoop, elke 30 seconden nieuw,
 * niet te delen en nooit over te zetten. Munten op een ander toestel staan er met de naam van dat toestel bij.
 * Anders munten kopen.
 */
export default function MuntenScreen() {
  const { colors } = useTheme();
  const status = useSessionStatus();
  const orders = useMyOrders();
  const tickets = tokenTickets(orders.data);
  const elsewhere = tokensElsewhere(orders.data);
  const buy = <Button label="Munten kopen" variant="secondary" onPress={() => router.push('/munten/kopen')} />;
  const elsewhereCard =
    elsewhere.length > 0 ? (
      <Card style={styles.card}>
        <AppText variant="cardTitle">Munten op een ander toestel</AppText>
        {elsewhere.map((t) => (
          <AppText key={t.id} variant="body" color={colors.textSecondary}>
            {`${t.quantity} munten (bestelling ${t.number}) zijn gekoppeld aan ${t.deviceName} en alleen daar af te halen.`}
          </AppText>
        ))}
        <AppText variant="caption" color={colors.textSecondary}>
          Munten werken alleen op het toestel waarop ze gekocht zijn en zijn niet over te zetten.
        </AppText>
      </Card>
    ) : null;

  if (status === 'signedIn' && tickets.length > 0) {
    return (
      <TicketScreen
        purpose="Tokens"
        tokenTickets={tickets}
        before={
          <View style={styles.before}>
            {buy}
            {elsewhereCard}
          </View>
        }
      />
    );
  }

  return (
    <Screen>
      <BackLink label="Home" />
      <LargeTitleHeader title="Munten" />
      <View style={styles.content}>
        {status === 'signedIn' && !orders.data ? (
          <QueryState query={orders} />
        ) : (
          <>
            {elsewhereCard}
            <Card style={styles.card}>
              <AppText variant="cardTitle">
                {status === 'signedIn' ? 'Geen munten af te halen op dit toestel' : 'Munten zijn voor leden'}
              </AppText>
              <AppText variant="body" color={colors.textSecondary}>
                Koop munten vooraf en haal ze op bij de kassa: daar wordt je munten-QR gescand en krijg je de munten.
                Aankoop is persoonsgebonden en werkt alleen op het toestel waarop je koopt.
              </AppText>
              {status === 'signedIn' ? buy : <Button label="Inloggen" onPress={() => router.push('/meer/inloggen')} />}
            </Card>
          </>
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 14, paddingBottom: 24 },
  before: { paddingHorizontal: 20, paddingBottom: 8, gap: 12 },
  card: { padding: 16, gap: 12 },
});
