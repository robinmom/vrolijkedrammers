import { useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { OrderCard } from '../../features/SaleParts';
import { loadGuestOrders, rememberOrder, salesKeys, useOrderStatus } from '../../features/sales';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, ErrorState, LargeTitleHeader, Screen } from '../../ui';

/**
 * Na het bestellen of via de link "terug naar de app" (fase 19b): de status van de bestelling. Terwijl Mollie de
 * betaling verwerkt, kijkt de app elke paar seconden opnieuw; daarna staat de QR hier en onder Mijn kaarten.
 * Afrekenen in de app onthoudt de bestelling zelf; een bestelling die alleen via een link binnenkomt (deeplink
 * drammers://kaarten/bestelling?id=…&t=…) komt pas na bevestiging onder Mijn kaarten.
 */
export default function BestellingScreen() {
  const { colors } = useTheme();
  const params = useLocalSearchParams<{ id: string; t: string }>();
  const order = useMemo(() => (params.id && params.t ? { id: params.id, token: params.t } : null), [params.id, params.t]);
  const status = useOrderStatus(order);
  const queryClient = useQueryClient();
  // Staat deze bestelling al op dit toestel? `null` zolang dat nog niet bekend is.
  const [known, setKnown] = useState<boolean | null>(null);

  useEffect(() => {
    if (!order) return;
    let active = true;
    void loadGuestOrders().then((orders) => {
      if (active) setKnown(orders.some((o) => o.id === order.id));
    });
    return () => {
      active = false;
    };
  }, [order]);

  const add = async () => {
    if (!order) return;
    await rememberOrder(order);
    setKnown(true);
    void queryClient.invalidateQueries({ queryKey: salesKeys.guestOrders });
  };

  useEffect(() => {
    if (status.data?.status === 'Confirmed') {
      void queryClient.invalidateQueries({ queryKey: salesKeys.myOrders });
      void queryClient.invalidateQueries({ queryKey: salesKeys.guestOrders });
    }
  }, [status.data?.status, queryClient]);

  const data = status.data;
  return (
    <Screen>
      <BackLink label="Kaarten" />
      <LargeTitleHeader title={data?.status === 'Confirmed' ? 'Gelukt!' : 'Je bestelling'} />
      <View style={styles.content}>
        {!data ? (
          status.isError ? (
            <ErrorState message="Deze bestelling laden lukt nu niet." action={{ label: 'Opnieuw', onPress: () => void status.refetch() }} />
          ) : (
            <ActivityIndicator accessibilityLabel="Laden" />
          )
        ) : (
          <>
            <AppText variant="body" color={colors.textSecondary} accessibilityLiveRegion="polite">
              {data.status === 'Confirmed'
                ? data.kind === 'Tokens'
                  ? 'Betaald. Haal je munten op bij de kassa met de munten-QR (tegel Munten op het beginscherm).'
                  : 'Je kaarten staan klaar. Je krijgt ze ook per e-mail.'
                : data.status === 'AwaitingPayment'
                  ? 'We wachten op de betaling. Dat duurt meestal een paar seconden.'
                  : data.status === 'Expired'
                    ? 'Er is niet (op tijd) betaald. De plaatsen zijn weer vrij.'
                    : 'Deze bestelling is geannuleerd.'}
            </AppText>
            {known === false ? (
              <Card style={styles.confirm}>
                <AppText variant="bodyStrong">Deze bestelling toevoegen?</AppText>
                <AppText variant="body" color={colors.textSecondary}>
                  Bestelling {data.number} staat nog niet op deze telefoon. Voeg hem alleen toe als het je eigen bestelling
                  is; daarna staat hij onder Mijn kaarten.
                </AppText>
                <Button label="Toevoegen aan Mijn kaarten" onPress={() => void add()} />
                <Button label="Niet toevoegen" variant="secondary" onPress={() => router.replace('/kaarten')} />
              </Card>
            ) : known ? (
              <>
                <OrderCard order={data} />
                <Button label="Naar Mijn kaarten" variant="secondary" onPress={() => router.replace('/kaarten/mijn')} />
              </>
            ) : (
              <ActivityIndicator accessibilityLabel="Laden" />
            )}
          </>
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16, paddingBottom: 24 },
  confirm: { gap: 12 },
});
