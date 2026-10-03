import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { queryKeys, useMyMember } from '../../api/queries';
import { useTheme } from '../../theme/ThemeProvider';
import {
  AppText,
  BackLink,
  Button,
  Card,
  CheckboxRow,
  LargeTitleHeader,
  QueryState,
  Screen,
  TextField,
} from '../../ui';

const MANDATE =
  'Ik geef CV De Vrolijke Drammers toestemming om doorlopend de contributie van deze rekening te incasseren. Een afschrijving kan ik binnen 8 weken via mijn bank laten terugboeken.';

/**
 * Gegevens wijzigen (fase 26): adres, e-mail, telefoon en eventueel een nieuw rekeningnummer. De ledenadministratie
 * beoordeelt elke wijziging; pas daarna is ze zichtbaar onder Mijn gegevens.
 */
export default function GegevensWijzigenScreen() {
  const member = useMyMember();
  return member.data ? (
    <ChangeForm member={member.data} />
  ) : (
    <Screen>
      <BackLink label="Mijn gegevens" />
      <LargeTitleHeader title="Gegevens wijzigen" />
      <View style={styles.content}>
        <QueryState query={member} />
      </View>
    </Screen>
  );
}

type MemberData = NonNullable<ReturnType<typeof useMyMember>['data']>;

function ChangeForm({ member: m }: { member: MemberData }) {
  const { colors } = useTheme();
  const queryClient = useQueryClient();
  const [form, setForm] = useState({
    addressLine: m.addressLine ?? '',
    postalCode: m.postalCode ?? '',
    city: m.city ?? '',
    email: m.email ?? '',
    phone: m.phone ?? '',
  });
  const [iban, setIban] = useState('');
  const [holder, setHolder] = useState('');
  const [mandate, setMandate] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  const set = (change: Partial<typeof form>) => setForm({ ...form, ...change });
  const wantsIban = iban.trim().length > 0;
  const valid = !wantsIban || (holder.trim().length > 0 && mandate);

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const { error: problem, response } = await api.POST('/api/v1/me/change-requests', {
        body: {
          addressLine: form.addressLine.trim() || null,
          postalCode: form.postalCode.trim() || null,
          city: form.city.trim() || null,
          email: form.email.trim() || null,
          phone: form.phone.trim() || null,
          mobilePhone: null,
          iban: wantsIban ? iban.trim() : null,
          accountHolder: wantsIban ? holder.trim() : null,
          mandateConsent: wantsIban && mandate,
        },
      });
      if (response.ok) {
        setSent(true);
        await queryClient.invalidateQueries({ queryKey: queryKeys.myMemberRequests });
      } else {
        setError(
          (problem as { detail?: string } | undefined)?.detail ?? 'Versturen lukt nu niet. Probeer het later opnieuw.',
        );
      }
    } catch {
      setError('Geen verbinding. Probeer het opnieuw.');
    } finally {
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <Screen>
        <BackLink label="Mijn gegevens" />
        <LargeTitleHeader title="Wijziging verstuurd" />
        <View style={styles.content}>
          <Card style={styles.card}>
            <AppText variant="body" accessibilityRole="alert">
              De ledenadministratie beoordeelt je wijziging. Na goedkeuring krijg je een e-mail en zie je de nieuwe
              gegevens onder Mijn gegevens.
            </AppText>
          </Card>
          <Button label="Terug naar Mijn gegevens" variant="secondary" onPress={() => router.back()} />
        </View>
      </Screen>
    );
  }

  return (
    <Screen>
      <BackLink label="Mijn gegevens" />
      <LargeTitleHeader title="Gegevens wijzigen" />
      <View style={styles.content}>
        <AppText variant="body" color={colors.textSecondary}>
          Pas aan wat er niet klopt. De ledenadministratie controleert elke wijziging voordat ze wordt doorgevoerd.
        </AppText>
        <TextField
          label="Straat en huisnummer"
          value={form.addressLine}
          onChangeText={(v) => set({ addressLine: v })}
          autoComplete="street-address"
          maxLength={150}
        />
        <TextField
          label="Postcode"
          value={form.postalCode}
          onChangeText={(v) => set({ postalCode: v })}
          autoCapitalize="characters"
          autoComplete="postal-code"
          maxLength={10}
        />
        <TextField label="Woonplaats" value={form.city} onChangeText={(v) => set({ city: v })} maxLength={50} />
        <TextField
          label="E-mailadres"
          value={form.email}
          onChangeText={(v) => set({ email: v })}
          keyboardType="email-address"
          autoCapitalize="none"
          autoComplete="email"
          autoCorrect={false}
          maxLength={150}
        />
        <TextField
          label="Telefoon"
          value={form.phone}
          onChangeText={(v) => set({ phone: v })}
          keyboardType="phone-pad"
          autoComplete="tel"
          maxLength={50}
        />
        <AppText variant="sectionHeader" accessibilityRole="header">
          Nieuw rekeningnummer
        </AppText>
        <AppText variant="caption" color={colors.textSecondary}>
          Alleen invullen als de contributie voortaan van een andere rekening moet komen.
        </AppText>
        <TextField
          label="IBAN"
          value={iban}
          onChangeText={setIban}
          autoCapitalize="characters"
          autoCorrect={false}
          maxLength={40}
        />
        {wantsIban ? (
          <>
            <TextField label="Naam rekeninghouder" value={holder} onChangeText={setHolder} maxLength={100} />
            <CheckboxRow label={MANDATE} checked={mandate} onChange={setMandate} />
          </>
        ) : null}
        {error ? (
          <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
            {error}
          </AppText>
        ) : null}
        <Button label={busy ? 'Even geduld…' : 'Wijziging versturen'} onPress={submit} disabled={!valid || busy} />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 14 },
  card: { padding: 16, gap: 10 },
});
