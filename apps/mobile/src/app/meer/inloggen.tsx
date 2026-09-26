import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { NoAccountError, signIn } from '../../auth/session';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, LargeTitleHeader, Screen } from '../../ui';

/**
 * Inloggen voor leden (fase 9): e-mailadres + eenmalige code via de inlogpagina van de vereniging. Bewust geen
 * "Registreren": een account ontstaat via "Ik ben al lid" of het bestuur (ADR-014).
 */
export default function InloggenScreen() {
  const { colors } = useTheme();
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  async function login() {
    setBusy(true);
    setMessage(null);
    try {
      if ((await signIn()) === 'success') {
        router.replace('/account');
      }
    } catch (error) {
      setMessage(
        error instanceof NoAccountError
          ? 'Voor dit e-mailadres is (nog) geen account. Ben je lid? Vraag hieronder een account aan.'
          : 'Inloggen lukt nu niet. Controleer je verbinding en probeer het opnieuw.',
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen>
      <BackLink label="Meer" />
      <LargeTitleHeader title="Inloggen" />
      <View style={styles.content}>
        <Card style={styles.card}>
          <AppText variant="sectionHeader" accessibilityRole="header">
            Voor leden
          </AppText>
          <AppText variant="body" color={colors.textSecondary}>
            Log in met het e-mailadres dat bij de vereniging bekend is. Je krijgt een code per e-mail; een wachtwoord is
            niet nodig.
          </AppText>
          <Button label={busy ? 'Even geduld…' : 'Inloggen'} onPress={login} disabled={busy} />
          {message ? (
            <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
              {message}
            </AppText>
          ) : null}
        </Card>

        <Card style={styles.card}>
          <AppText variant="sectionHeader" accessibilityRole="header">
            Nog geen account?
          </AppText>
          <AppText variant="body" color={colors.textSecondary}>
            Ben je al lid? Vraag dan een account aan met je lidnummer en e-mailadres.
          </AppText>
          <Button
            label="Account aanvragen"
            variant="secondary"
            onPress={() => router.push('/meer/account-aanvragen')}
          />
          <AppText variant="body" color={colors.textSecondary}>
            Nog geen lid? Kijk bij Lid worden.
          </AppText>
          <Button label="Lid worden" variant="secondary" onPress={() => router.push('/meer/lid-worden')} />
        </Card>
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16 },
  card: { padding: 16, gap: 12 },
});
