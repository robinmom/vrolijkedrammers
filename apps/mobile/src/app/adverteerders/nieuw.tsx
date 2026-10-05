import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { queryKeys, useMyAdvertisers } from '../../api/queries';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, CheckboxRow, FilterChips, LargeTitleHeader, Screen, TextField } from '../../ui';

type Kind = 'Advertisement' | 'FreeGift' | 'Gift';
type Payment = 'Mandate' | 'Cash';

const MANDATE =
  'De adverteerder geeft CV De Vrolijke Drammers toestemming om het bedrag van deze rekening te incasseren. Een afschrijving kan binnen 8 weken via de bank worden teruggeboekt.';

/**
 * Nieuwe adverteerder (fase 27b-2): de collectant meldt een nieuwe adverteerder aan; die staat meteen op opgehaald
 * voor dit jaar en het bestuur kijkt de gegevens na in het portal.
 */
export default function NieuweAdverteerderScreen() {
  const { colors } = useTheme();
  const queryClient = useQueryClient();
  const mine = useMyAdvertisers();
  const [form, setForm] = useState({ companyName: '', contactName: '', phone: '', email: '', addressLine: '', postalCode: '', city: 'Loil', amount: '' });
  const [kind, setKind] = useState<Kind>('Advertisement');
  const [payment, setPayment] = useState<Payment>('Mandate');
  const [iban, setIban] = useState('');
  const [consent, setConsent] = useState(false);
  const [received, setReceived] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  const set = (change: Partial<typeof form>) => setForm({ ...form, ...change });
  const amount = Number(form.amount.replace(',', '.'));
  const valid =
    form.companyName.trim().length > 0 &&
    form.amount.trim().length > 0 &&
    !Number.isNaN(amount) &&
    amount >= 0 &&
    (payment === 'Cash' || (iban.trim().length > 0 && consent));

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const { response, error: problem } = await api.POST('/api/v1/me/advertisers', {
        body: {
          companyName: form.companyName.trim(),
          contactName: form.contactName.trim() || null,
          phone: form.phone.trim() || null,
          email: form.email.trim() || null,
          addressLine: form.addressLine.trim() || null,
          postalCode: form.postalCode.trim() || null,
          city: form.city.trim() || null,
          kind,
          payment,
          amount,
          iban: payment === 'Mandate' ? iban.trim() : null,
          mandateConsent: payment === 'Mandate' && consent,
          note: null,
          cashReceived: payment === 'Cash' && received,
        },
      });
      if (response.ok) {
        setDone(true);
        await queryClient.invalidateQueries({ queryKey: queryKeys.myAdvertisers });
      } else {
        setError((problem as { detail?: string } | undefined)?.detail ?? 'Opslaan lukt nu niet. Probeer het later opnieuw.');
      }
    } catch {
      setError('Geen verbinding. Probeer het opnieuw.');
    } finally {
      setBusy(false);
    }
  }

  if (done) {
    return (
      <Screen>
        <BackLink label="Adverteerders" />
        <LargeTitleHeader title="Adverteerder toegevoegd" />
        <View style={styles.content}>
          <Card style={styles.card}>
            <AppText variant="body" accessibilityRole="alert">
              {form.companyName.trim()} staat op opgehaald voor {mine.data ? `${mine.data.year - 1}/${mine.data.year}` : 'dit jaar'}. Het bestuur kijkt de gegevens na.
            </AppText>
          </Card>
          <Button label="Terug naar mijn adverteerders" variant="secondary" onPress={() => router.back()} />
        </View>
      </Screen>
    );
  }

  return (
    <Screen>
      <BackLink label="Adverteerders" />
      <LargeTitleHeader title="Nieuwe adverteerder" />
      <View style={styles.content}>
        <TextField label="Naam bedrijf" value={form.companyName} onChangeText={(v) => set({ companyName: v })} maxLength={200} />
        <TextField label="Contactpersoon" value={form.contactName} onChangeText={(v) => set({ contactName: v })} maxLength={150} />
        <TextField label="Telefoon" value={form.phone} onChangeText={(v) => set({ phone: v })} keyboardType="phone-pad" maxLength={30} />
        <TextField
          label="E-mailadres"
          value={form.email}
          onChangeText={(v) => set({ email: v })}
          keyboardType="email-address"
          autoCapitalize="none"
          autoCorrect={false}
          maxLength={254}
        />
        <TextField label="Straat en huisnummer" value={form.addressLine} onChangeText={(v) => set({ addressLine: v })} maxLength={200} />
        <TextField label="Postcode" value={form.postalCode} onChangeText={(v) => set({ postalCode: v })} autoCapitalize="characters" maxLength={10} />
        <TextField label="Plaats" value={form.city} onChangeText={(v) => set({ city: v })} maxLength={100} />
        <AppText variant="sectionHeader" accessibilityRole="header">
          Bijdrage
        </AppText>
        <FilterChips<Kind>
          accessibilityLabel="Soort"
          options={[
            { value: 'Advertisement', label: 'Advertentie' },
            { value: 'FreeGift', label: 'Vrije gift' },
            { value: 'Gift', label: 'Gift' },
          ]}
          selected={kind}
          onChange={setKind}
        />
        <TextField label="Bedrag (€)" value={form.amount} onChangeText={(v) => set({ amount: v })} keyboardType="decimal-pad" maxLength={10} />
        <FilterChips<Payment>
          accessibilityLabel="Betaling"
          options={[
            { value: 'Mandate', label: 'Machtiging' },
            { value: 'Cash', label: 'Contant' },
          ]}
          selected={payment}
          onChange={setPayment}
        />
        {payment === 'Mandate' ? (
          <>
            <TextField label="IBAN" value={iban} onChangeText={setIban} autoCapitalize="characters" autoCorrect={false} maxLength={40} />
            <CheckboxRow label={MANDATE} checked={consent} onChange={setConsent} />
          </>
        ) : (
          <CheckboxRow label="Geld contant ontvangen" checked={received} onChange={setReceived} />
        )}
        {error ? (
          <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
            {error}
          </AppText>
        ) : null}
        <Button label={busy ? 'Even geduld…' : 'Adverteerder toevoegen'} onPress={() => void submit()} disabled={!valid || busy} />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 14, paddingBottom: 24 },
  card: { padding: 16, gap: 10 },
});
