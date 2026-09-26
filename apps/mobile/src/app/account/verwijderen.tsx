import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { clearLocalSession } from '../../auth/session';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, LargeTitleHeader, Screen, TextField } from '../../ui';

const CONFIRMATION = 'VERWIJDEREN';

/**
 * Account verwijderen (fase 9, store-eis): verwijdert het app-account en het inlogaccount. Het lidmaatschap zelf
 * blijft bestaan; opzeggen gaat via het secretariaat.
 */
export default function AccountVerwijderenScreen() {
  const { colors } = useTheme();
  const [typed, setTyped] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function remove() {
    setBusy(true);
    setError(null);
    const { response } = await api
      .DELETE('/api/v1/me', { body: { confirmation: CONFIRMATION } })
      .catch(() => ({ response: null }));
    setBusy(false);
    if (response?.ok) {
      await clearLocalSession();
      router.replace('/meer');
    } else {
      setError('Verwijderen lukt nu niet. Probeer het later opnieuw of neem contact op met het secretariaat.');
    }
  }

  return (
    <Screen>
      <BackLink label="Mijn gegevens" />
      <LargeTitleHeader title="Account verwijderen" />
      <View style={styles.content}>
        <Card style={styles.card}>
          <AppText variant="body">
            Je app-account en je inlog worden verwijderd, en je wordt op al je apparaten uitgelogd. Je lidmaatschap van
            De Vrolijke Drammers blijft bestaan; wil je opzeggen, neem dan contact op met het secretariaat.
          </AppText>
          <AppText variant="body" color={colors.textSecondary}>
            Later kun je altijd opnieuw een account aanvragen.
          </AppText>
          <TextField
            label={`Typ ${CONFIRMATION} om te bevestigen`}
            value={typed}
            onChangeText={setTyped}
            autoCapitalize="characters"
            autoCorrect={false}
          />
          <Button
            label={busy ? 'Even geduld…' : 'Account verwijderen'}
            onPress={remove}
            disabled={typed !== CONFIRMATION || busy}
          />
          {error ? (
            <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
              {error}
            </AppText>
          ) : null}
        </Card>
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16 },
  card: { padding: 16, gap: 14 },
});
