import { useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import * as WebBrowser from 'expo-web-browser';
import { useState } from 'react';
import { Alert, StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { useMe } from '../../api/queries';
import { useSessionStatus } from '../../auth/useSession';
import {
  formatDay,
  formatEuro,
  problemCode,
  problemMessage,
  rememberOrder,
  salesKeys,
  useSaleCatalog,
} from '../../features/sales';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, LargeTitleHeader, QueryState, Screen, TextField } from '../../ui';

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Afrekenen (fase 19b, Figma "iOS / 4 Afrekenen"): overzicht, gegevens van de koper (een lid is al bekend) en betalen
 * met iDEAL via Mollie in de browser. Gratis groepskaarten zijn direct geldig. Met `wachtlijst=1` op de wachtlijst.
 */
export default function AfrekenenScreen() {
  const { colors } = useTheme();
  const params = useLocalSearchParams<{ product: string; member?: string; paid?: string; wachtlijst?: string }>();
  const status = useSessionStatus();
  const me = useMe();
  const catalog = useSaleCatalog();
  const queryClient = useQueryClient();
  const product = catalog.data?.products.find((p) => p.id === params.product);
  const member = Number(params.member ?? 0);
  const paid = Number(params.paid ?? 0);
  const waitlist = params.wachtlijst === '1';
  const signedIn = status === 'signedIn';
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [phone, setPhone] = useState('');
  const [remark, setRemark] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const amount = paid * (product?.priceCents ?? 0);
  const valid = signedIn || (name.trim().length > 0 && EMAIL.test(email.trim()));

  const body = () => ({
    productId: params.product,
    memberQuantity: member,
    paidQuantity: paid,
    buyerName: signedIn ? null : name.trim(),
    buyerEmail: signedIn ? null : email.trim(),
    buyerPhone: phone.trim() || null,
    remark: remark.trim() || null,
    channel: 'App' as const,
  });

  async function joinWaitlist() {
    const { error: problem, response } = await api.POST('/api/v1/sales/waitlist', { body: body() });
    if (!response.ok) {
      setError(problemMessage(problem, response.status));
      return;
    }
    setDone(
      `Je staat op de wachtlijst voor ${product?.name ?? 'de pronkzitting'}. Komt er plek, dan krijg je ${signedIn ? 'een melding en ' : ''}een e-mail met een betaallink die 48 uur geldig is.`,
    );
  }

  async function order() {
    const { data, error: problem, response } = await api.POST('/api/v1/sales/orders', { body: body() });
    if (!response.ok || !data) {
      if (problemCode(problem) === 'SOLD_OUT') {
        Alert.alert('Net vol', problemMessage(problem, response.status), [
          { text: 'Op de wachtlijst', onPress: () => void run(joinWaitlist) },
          { text: 'Terug', style: 'cancel' },
        ]);
      } else {
        setError(problemMessage(problem, response.status));
      }
      return;
    }
    await rememberOrder({ id: data.orderId, token: data.token });
    await queryClient.invalidateQueries({ queryKey: salesKeys.catalog });
    router.replace(`/kaarten/bestelling?id=${data.orderId}&t=${encodeURIComponent(data.token)}`);
    if (data.checkoutUrl) {
      // Betalen in de browser van het toestel; de bestelpagina stuurt na het betalen terug naar de app.
      await WebBrowser.openAuthSessionAsync(data.checkoutUrl, 'drammers://kaarten/bestelling');
    }
  }

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await action();
    } catch {
      setError('Geen verbinding. Probeer het opnieuw.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen>
      <BackLink label={product?.kind === 'Pronkzitting' ? 'Pronkzitting' : 'Terug'} />
      <LargeTitleHeader title={waitlist ? 'Wachtlijst' : 'Afrekenen'} />
      <View style={styles.content}>
        {!product ? (
          <QueryState query={catalog} />
        ) : done ? (
          <Card style={styles.card}>
            <AppText variant="body" accessibilityRole="alert">
              {done}
            </AppText>
            <Button label="Naar Kaarten" onPress={() => router.replace('/kaarten')} />
          </Card>
        ) : (
          <>
            <Card style={styles.card}>
              <AppText variant="cardTitle">
                {[product.name, formatDay(product.date)].filter(Boolean).join(' · ')}
              </AppText>
              {member > 0 ? (
                <Line label={`${member} × kaart ${catalog.data?.group?.groupName ?? 'groep'} (lid)`} value="gratis" />
              ) : null}
              {paid > 0 ? (
                <Line
                  label={`${paid} × ${product.kind === 'Tokens' ? 'munt' : member > 0 ? 'losse kaart' : 'kaart'} à ${formatEuro(product.priceCents)}`}
                  value={formatEuro(amount)}
                />
              ) : null}
              <View style={[styles.divider, { backgroundColor: colors.border }]} />
              <Line label="Te betalen" value={waitlist ? 'pas bij een plek' : formatEuro(amount)} strong />
            </Card>

            {signedIn ? (
              <AppText variant="body" color={colors.textSecondary}>
                Ingelogd als {me.data?.displayName ?? 'lid'}. Je kaarten komen in de app en per e-mail.
              </AppText>
            ) : (
              <>
                {product.kind !== 'Tokens' ? (
                  <AppText variant="body" color={colors.textSecondary}>
                    Ben je lid?{' '}
                    <AppText variant="link" onPress={() => router.push('/meer/inloggen')} accessibilityRole="link">
                      Log in
                    </AppText>
                    , dan zie je de gratis kaarten voor je groep.
                  </AppText>
                ) : null}
                <TextField label="Naam" value={name} onChangeText={setName} autoComplete="name" maxLength={200} />
                <TextField
                  label="E-mailadres"
                  value={email}
                  onChangeText={setEmail}
                  keyboardType="email-address"
                  autoCapitalize="none"
                  autoComplete="email"
                  autoCorrect={false}
                  maxLength={254}
                />
              </>
            )}
            <TextField label="Telefoon (optioneel)" value={phone} onChangeText={setPhone} keyboardType="phone-pad" maxLength={40} />
            {product.kind === 'Pronkzitting' ? (
              <TextField
                label="Opmerking of wensen (optioneel)"
                hint="Bijvoorbeeld bij wie jullie willen zitten."
                value={remark}
                onChangeText={setRemark}
                maxLength={500}
              />
            ) : null}
            <AppText variant="caption" color={colors.textSecondary}>
              {product.kind === 'Tokens'
                ? 'Aankoop is persoonsgebonden: je haalt de munten zelf op bij de kassa met de munten-QR. Niet-leden kunnen geen munten kopen.'
                : 'Niet-leden betalen altijd met iDEAL. Je kaarten komen in de app en per e-mail.'}{' '}
              Kaarten en munten worden niet terugbetaald.
            </AppText>
            {error ? (
              <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
                {error}
              </AppText>
            ) : null}
            <Button
              label={
                busy
                  ? 'Even geduld…'
                  : waitlist
                    ? 'Op de wachtlijst'
                    : amount > 0
                      ? `Betalen met iDEAL · ${formatEuro(amount)}`
                      : 'Bestellen'
              }
              onPress={() => void run(waitlist ? joinWaitlist : order)}
              disabled={!valid || busy}
            />
            {!waitlist && amount > 0 ? (
              <AppText variant="caption" color={colors.textSecondary} style={styles.center}>
                Veilig betalen via Mollie
              </AppText>
            ) : null}
          </>
        )}
      </View>
    </Screen>
  );
}

function Line({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  const { colors } = useTheme();
  return (
    <View style={styles.line}>
      <AppText variant={strong ? 'bodyStrong' : 'body'} color={strong ? undefined : colors.textSecondary} style={styles.full}>
        {label}
      </AppText>
      <AppText variant={strong ? 'cardTitle' : 'bodyStrong'}>{value}</AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 14, paddingBottom: 24 },
  card: { padding: 16, gap: 8 },
  line: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  full: { flex: 1 },
  divider: { height: StyleSheet.hairlineWidth, marginVertical: 4 },
  center: { textAlign: 'center' },
});
