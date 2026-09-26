import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api, apiBaseUrl } from '../../api/client';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, LargeTitleHeader, Screen, TextField } from '../../ui';

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Melding na het versturen. Ook bij een time-out: de API kan er na een rustige periode lang over doen (de database
 * wordt dan eerst gestart) en verwerkt de aanvraag dan alsnog; een dubbele aanvraag binnen 24 uur telt niet.
 */
export const PENDING_MESSAGE =
  'Je aanvraag is in behandeling. Klopt alles met de ledenadministratie, dan ontvang je binnen enkele minuten een e-mail met uitleg om in te loggen. Kijk ook in je map met ongewenste e-mail. Zo niet, dan kijkt het bestuur ernaar en hoor je van ons.';

/**
 * "Ik ben al lid" (fase 9, ADR-014): lidnummer + e-mailadres. Iedereen krijgt dezelfde melding; klopt alles met de
 * ledenadministratie, dan volgt een welkomstmail, anders kijkt het bestuur ernaar.
 */
export default function AccountAanvragenScreen() {
  const { colors } = useTheme();
  const [memberNumber, setMemberNumber] = useState('');
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const valid = memberNumber.trim().length > 0 && EMAIL.test(email.trim());

  // Maakt de API (en de database) alvast wakker terwijl het lid het formulier invult.
  useEffect(() => {
    fetch(`${apiBaseUrl}/health/ready`).catch(() => undefined);
  }, []);

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const { response } = await api.POST('/api/v1/account-requests', {
        body: { memberNumber: memberNumber.trim(), email: email.trim() },
      });
      if (response.status === 429) {
        setError('Te veel aanvragen vanaf dit netwerk. Probeer het over tien minuten opnieuw.');
      } else if (response.status === 400) {
        setError('Controleer je lidnummer en e-mailadres.');
      } else if (response.ok) {
        setDone(PENDING_MESSAGE);
      } else {
        setError('Aanvragen lukt nu niet. Probeer het later opnieuw.');
      }
    } catch {
      // Geen antwoord (bijv. time-out): de aanvraag is meestal wel aangekomen en wordt verwerkt.
      setDone(PENDING_MESSAGE);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen>
      <BackLink label="Inloggen" />
      <LargeTitleHeader title="Account aanvragen" />
      <View style={styles.content}>
        <Card style={styles.card}>
          {done ? (
            <AppText variant="body" accessibilityRole="alert">
              {done}
            </AppText>
          ) : (
            <>
              <AppText variant="body" color={colors.textSecondary}>
                Vul je lidnummer en het e-mailadres in dat bij de vereniging bekend is. Je lidnummer staat op de
                contributiefactuur.
              </AppText>
              <TextField
                label="Lidnummer"
                value={memberNumber}
                onChangeText={setMemberNumber}
                autoCapitalize="none"
                autoCorrect={false}
                maxLength={15}
              />
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
              <Button label={busy ? 'Even geduld…' : 'Account aanvragen'} onPress={submit} disabled={!valid || busy} />
              {error ? (
                <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
                  {error}
                </AppText>
              ) : null}
            </>
          )}
        </Card>
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16 },
  card: { padding: 16, gap: 14 },
});
