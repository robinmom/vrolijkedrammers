import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { completeSignInFromRedirect, NoAccountError } from '../auth/session';
import { useTheme } from '../theme/ThemeProvider';
import { AppText, Button, Screen } from '../ui';

/**
 * Terugkeer van de inlogpagina (drammers://auth?code=…). Op Android komt dit adres soms als gewone link binnen, bijv. na
 * "Doorgaan" op de controlepagina van Microsoft of als de app opnieuw is gestart. Wacht de app zelf al op het antwoord,
 * dan gaat deze pagina meteen terug; anders rondt hij de aanmelding af.
 */
export default function AuthReturnScreen() {
  const { colors } = useTheme();
  const params = useLocalSearchParams<{ code?: string; state?: string }>();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    completeSignInFromRedirect({ code: params.code, state: params.state })
      .then(() => {
        if (active) {
          if (router.canGoBack()) router.back();
          else router.replace('/');
        }
      })
      .catch((e: unknown) => {
        if (active) {
          setError(
            e instanceof NoAccountError
              ? 'Er is (nog) geen account voor dit e-mailadres. Vraag het secretariaat om een account.'
              : 'Inloggen is niet gelukt. Probeer het opnieuw via Meer → Inloggen.',
          );
        }
      });
    return () => {
      active = false;
    };
  }, [params.code, params.state]);

  return (
    <Screen>
      <View style={styles.content}>
        {error ? (
          <>
            <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
              {error}
            </AppText>
            <Button label="Naar Meer" onPress={() => router.replace('/meer')} />
          </>
        ) : (
          <AppText variant="body" color={colors.textSecondary}>
            Bezig met inloggen…
          </AppText>
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { padding: 20, gap: 12 },
});
