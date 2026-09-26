import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, LargeTitleHeader, Screen, TextField } from '../../ui';

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

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

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const { data, response } = await api.POST('/api/v1/account-requests', {
        body: { memberNumber: memberNumber.trim(), email: email.trim() },
      });
      if (response.status === 429) {
        setError('Te veel aanvragen vanaf dit netwerk. Probeer het over tien minuten opnieuw.');
      } else if (!response.ok || !data) {
        setError('Aanvragen lukt nu niet. Controleer je gegevens en je verbinding.');
      } else {
        setDone(data.message);
      }
    } catch {
      setError('Aanvragen lukt nu niet. Controleer je verbinding en probeer het opnieuw.');
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
