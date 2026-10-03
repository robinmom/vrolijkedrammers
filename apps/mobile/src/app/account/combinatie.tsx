import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { queryKeys, useMyMemberRequests } from '../../api/queries';
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
 * Combinatie verbreken (fase 26): jullie zijn samen lid (combinatie) en willen ieder een eigen lidmaatschap. Beiden
 * geven akkoord; het tweede lid gaat zelf betalen en geeft daarvoor een IBAN. Daarna beslist de ledenadministratie.
 */
export default function CombinatieScreen() {
  const { colors } = useTheme();
  const queryClient = useQueryClient();
  const requests = useMyMemberRequests();
  const [iban, setIban] = useState('');
  const [holder, setHolder] = useState('');
  const [mandate, setMandate] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const c = requests.data?.combination;

  async function call(
    path: '/api/v1/me/combination-break' | '/api/v1/me/combination-break/agree' | '/api/v1/me/combination-break/cancel',
  ) {
    setBusy(true);
    setError(null);
    try {
      const bank = c?.ibanRequiredFromMe
        ? { iban: iban.trim() || null, accountHolder: holder.trim() || null, mandateConsent: mandate }
        : { iban: null, accountHolder: null, mandateConsent: false };
      const { error: problem, response } =
        path === '/api/v1/me/combination-break/cancel' ? await api.POST(path) : await api.POST(path, { body: bank });
      if (response.ok) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.myMemberRequests });
      } else {
        setError(
          (problem as { detail?: string } | undefined)?.detail ?? 'Dit lukt nu niet. Probeer het later opnieuw.',
        );
      }
    } catch {
      setError('Geen verbinding. Probeer het opnieuw.');
    } finally {
      setBusy(false);
    }
  }

  const bankFields = c?.ibanRequiredFromMe ? (
    <Card style={styles.card}>
      <AppText variant="bodyStrong">Jouw rekening</AppText>
      <AppText variant="caption" color={colors.textSecondary}>
        Na het verbreken betaal je zelf de contributie voor één lid. Vul de rekening in waarvan we die mogen incasseren.
      </AppText>
      <TextField
        label="IBAN"
        value={iban}
        onChangeText={setIban}
        autoCapitalize="characters"
        autoCorrect={false}
        maxLength={40}
      />
      <TextField label="Naam rekeninghouder" value={holder} onChangeText={setHolder} maxLength={100} />
      <CheckboxRow label={MANDATE} checked={mandate} onChange={setMandate} />
    </Card>
  ) : null;
  const bankValid = !c?.ibanRequiredFromMe || (iban.trim().length > 0 && holder.trim().length > 0 && mandate);

  let body: React.ReactNode = null;
  if (c && !c.breakStatus) {
    body = (
      <>
        <AppText variant="body">
          Je bent samen met {c.otherName} lid (combinatie). Willen jullie ieder een eigen lidmaatschap? Dan betaalt
          ieder de contributie voor één lid. Jullie jaren lid blijven gelijk.
        </AppText>
        <AppText variant="body" color={colors.textSecondary}>
          Na jouw verzoek moet {c.otherName} in de app akkoord geven. Daarna beoordeelt de ledenadministratie het.
        </AppText>
        {bankFields}
        <Button
          label={busy ? 'Even geduld…' : 'Combinatie verbreken'}
          onPress={() => call('/api/v1/me/combination-break')}
          disabled={busy || !bankValid}
        />
      </>
    );
  } else if (c?.breakStatus === 'AwaitingAgreement' && !c.iAgreed) {
    body = (
      <>
        <AppText variant="body" accessibilityRole="alert">
          {c.otherName} wil jullie combinatie verbreken, zodat ieder een eigen lidmaatschap heeft. Ben je het ermee
          eens?
        </AppText>
        {bankFields}
        <Button
          label={busy ? 'Even geduld…' : 'Akkoord'}
          onPress={() => call('/api/v1/me/combination-break/agree')}
          disabled={busy || !bankValid}
        />
        <Button
          label="Niet akkoord"
          variant="secondary"
          onPress={() => call('/api/v1/me/combination-break/cancel')}
          disabled={busy}
        />
      </>
    );
  } else if (c?.breakStatus) {
    body = (
      <>
        <AppText variant="body" accessibilityRole="alert">
          {c.breakStatus === 'AwaitingAgreement'
            ? `Je verzoek is verstuurd. Nu moet ${c.otherName} akkoord geven in de app.`
            : 'Jullie hebben allebei akkoord gegeven. De ledenadministratie beoordeelt het verzoek; je krijgt een e-mail zodra het is verwerkt.'}
        </AppText>
        <Button
          label="Verzoek intrekken"
          variant="secondary"
          onPress={() => call('/api/v1/me/combination-break/cancel')}
          disabled={busy}
        />
      </>
    );
  } else if (requests.data) {
    body = <AppText variant="body">Je lidmaatschap is geen combinatie.</AppText>;
  }

  return (
    <Screen>
      <BackLink label="Mijn gegevens" />
      <LargeTitleHeader title="Combinatie" />
      <View style={styles.content}>
        <QueryState query={requests} />
        {body}
        {error ? (
          <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
            {error}
          </AppText>
        ) : null}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 14 },
  card: { padding: 16, gap: 10 },
});
