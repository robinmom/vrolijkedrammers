import { brand } from '@drammers/design-tokens';
import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { queryKeys } from '../../api/queries';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, LargeTitleHeader, Screen, TextField } from '../../ui';

type Relationship = 'Parent' | 'Caregiver';

/**
 * Kind koppelen aanvragen (fase 17, Figma schermen 4 en 5): voor- en achternaam van het kind. Het bestuur controleert
 * het verzoek in het portal; pas daarna staat het kind onder Mijn kinderen.
 */
export default function KindKoppelenScreen() {
  const { colors } = useTheme();
  const queryClient = useQueryClient();
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [relationship, setRelationship] = useState<Relationship>('Parent');
  const [phone, setPhone] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState<string | null>(null);
  const valid = firstName.trim().length > 0 && lastName.trim().length > 0;

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const { error: problem, response } = await api.POST('/api/v1/me/guardian-requests', {
        body: {
          childFirstName: firstName.trim(),
          childLastName: lastName.trim(),
          relationship,
          phone: phone.trim() || null,
        },
      });
      if (response.ok) {
        setSent(`${firstName.trim()} ${lastName.trim()}`);
        await queryClient.invalidateQueries({ queryKey: queryKeys.myGuardianRequests });
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
        <BackLink label="Mijn kinderen" />
        <LargeTitleHeader title="Verzoek verstuurd" />
        <View style={styles.content}>
          <Card style={[styles.card, styles.center]}>
            <View style={[styles.check, { backgroundColor: colors.tintGreen }]}>
              <AppText variant="sectionHeader" color={colors.successText}>
                ✓
              </AppText>
            </View>
            <AppText variant="sectionHeader" accessibilityRole="header" style={styles.centerText}>
              We hebben je verzoek ontvangen
            </AppText>
            <AppText variant="body" color={colors.textSecondary} style={styles.centerText} accessibilityRole="alert">
              Het bestuur bekijkt je verzoek voor {sent}. Je krijgt een melding zodra het is goedgekeurd; daarna staat
              je kind onder Mijn kinderen.
            </AppText>
          </Card>
          <Button label="Terug naar Mijn kinderen" variant="secondary" onPress={() => router.back()} />
        </View>
      </Screen>
    );
  }

  return (
    <Screen>
      <BackLink label="Mijn kinderen" />
      <LargeTitleHeader title="Kind koppelen" />
      <View style={styles.content}>
        <AppText variant="body" color={colors.textSecondary}>
          Vul de naam van je kind in zoals die bij de vereniging bekend is. Het bestuur controleert je verzoek; pas
          daarna zie je je kind hier.
        </AppText>
        <TextField
          label="Voornaam kind"
          value={firstName}
          onChangeText={setFirstName}
          autoCapitalize="words"
          maxLength={50}
        />
        <TextField
          label="Achternaam kind"
          value={lastName}
          onChangeText={setLastName}
          autoCapitalize="words"
          maxLength={80}
        />
        <View style={styles.field}>
          <AppText variant="bodyStrong">Jij bent</AppText>
          <View
            style={[styles.segments, { backgroundColor: colors.border }]}
            accessibilityRole="radiogroup"
            accessibilityLabel="Jij bent"
          >
            {(
              [
                ['Parent', 'Ouder'],
                ['Caregiver', 'Verzorger'],
              ] as const
            ).map(([value, label]) => (
              <Pressable
                key={value}
                onPress={() => setRelationship(value)}
                accessibilityRole="radio"
                accessibilityState={{ checked: relationship === value }}
                style={[styles.segment, relationship === value && { backgroundColor: colors.surface }]}
              >
                <AppText variant={relationship === value ? 'bodyStrong' : 'body'}>{label}</AppText>
              </Pressable>
            ))}
          </View>
        </View>
        <TextField
          label="Telefoonnummer (optioneel)"
          value={phone}
          onChangeText={setPhone}
          keyboardType="phone-pad"
          autoComplete="tel"
          maxLength={30}
        />
        <Card style={[styles.card, { backgroundColor: colors.tintBlue }]}>
          <AppText variant="caption">
            Het bestuur kan contact met je opnemen om te controleren dat je de ouder of verzorger bent. Een kind heeft
            maximaal 2 ouders of verzorgers in de app.
          </AppText>
        </Card>
        {error ? (
          <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
            {error}
          </AppText>
        ) : null}
        <Button label={busy ? 'Even geduld…' : 'Verzoek versturen'} onPress={submit} disabled={!valid || busy} />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16, paddingBottom: 24 },
  card: { padding: 16, gap: 12 },
  center: { alignItems: 'center' },
  centerText: { textAlign: 'center' },
  check: {
    width: 64,
    height: 64,
    borderRadius: 32,
    alignItems: 'center',
    justifyContent: 'center',
    borderColor: brand.green,
  },
  field: { gap: 6 },
  segments: { flexDirection: 'row', borderRadius: 12, padding: 3 },
  segment: { flex: 1, alignItems: 'center', paddingVertical: 9, borderRadius: 10 },
});
