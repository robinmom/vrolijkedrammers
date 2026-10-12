import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { NoAccountError, signIn } from '../../auth/session';
import { enablePush, getPushPermission } from '../../features/push';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, LargeTitleHeader, Screen } from '../../ui';

/**
 * Inloggen voor leden: e-mailadres + wachtwoord via de inlogpagina van de vereniging (2026-10-12). Bewust geen
 * "Registreren" of "Account aanvragen": elk lid krijgt een account met een welkomstmail (ADR-014); de eerste keer kiest het
 * lid zelf een wachtwoord via "Wachtwoord vergeten?".
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
        // Logisch moment om push te vragen (fase 10): net ingelogd. Eerder geweigerd = niet opnieuw vragen.
        if ((await getPushPermission().catch(() => 'unavailable')) === 'undetermined') {
          void enablePush().catch(() => undefined);
        }
      }
    } catch (error) {
      setMessage(
        error instanceof NoAccountError
          ? 'Voor dit e-mailadres is (nog) geen account. Ben je lid? Neem dan contact op met het secretariaat.'
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
            Log in met het e-mailadres dat bij de vereniging bekend is. De eerste keer? Kies op de inlogpagina
            &quot;Wachtwoord vergeten?&quot;: je krijgt een code per e-mail en kiest zelf een wachtwoord.
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
            Elk lid krijgt een account en een welkomstmail. Geen mail gehad of lukt inloggen niet? Neem contact op met
            het secretariaat.
          </AppText>
          <Button label="Contact" variant="secondary" onPress={() => router.push('/meer/contact')} />
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
