import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { fotoTekst, mandaatTekst, privacyTekst } from '../../content/static';
import { ageOn, parseDutchDate } from '../../features/membership';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, CheckboxRow, LargeTitleHeader, Screen, TextField } from '../../ui';

type Step = 'form' | 'code' | 'done';

interface FormState {
  firstName: string;
  namePrefix: string;
  lastName: string;
  birthDate: string;
  gender: '' | 'm' | 'v';
  addressLine: string;
  postalCode: string;
  city: string;
  email: string;
  phone: string;
  guardianName: string;
  guardianPhone: string;
  iban: string;
  accountHolder: string;
  mandateConsent: boolean;
  privacyConsent: boolean;
  photoConsent: boolean;
}

const EMPTY: FormState = {
  firstName: '',
  namePrefix: '',
  lastName: '',
  birthDate: '',
  gender: '',
  addressLine: '',
  postalCode: '',
  city: '',
  email: '',
  phone: '',
  guardianName: '',
  guardianPhone: '',
  iban: '',
  accountHolder: '',
  mandateConsent: false,
  privacyConsent: false,
  photoConsent: false,
};

/** Foutmelding uit ProblemDetails (de API legt uit wat er mis is), anders een algemene tekst. */
function problemText(status: number, error: unknown): string {
  if (status === 429) {
    return 'Te veel pogingen vanaf dit netwerk. Probeer het over tien minuten opnieuw.';
  }
  const detail = (error as { detail?: string } | undefined)?.detail;
  return (
    detail ?? (status === 400 ? 'Controleer de ingevulde gegevens.' : 'Er ging iets mis. Probeer het later opnieuw.')
  );
}

/**
 * Lid worden (fase 9b, ADR-014): formulier → code per e-mail → ingediend. Het bestuur beoordeelt elke aanmelding;
 * onder de 16 vult de ouder/verzorger het formulier in en krijgt die het account.
 */
export default function AanmeldenScreen() {
  const { colors } = useTheme();
  const [step, setStep] = useState<Step>('form');
  const [form, setForm] = useState<FormState>(EMPTY);
  const [applicationId, setApplicationId] = useState<string | null>(null);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set =
    <K extends keyof FormState>(key: K) =>
    (value: FormState[K]) =>
      setForm((current) => ({ ...current, [key]: value }));

  const birthDate = parseDutchDate(form.birthDate);
  const minor = birthDate !== null && ageOn(birthDate, new Date()) < 16;
  const complete =
    Boolean(
      form.firstName.trim() &&
      form.lastName.trim() &&
      birthDate &&
      form.addressLine.trim() &&
      form.postalCode.trim() &&
      form.city.trim(),
    ) &&
    /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email.trim()) &&
    Boolean(form.iban.trim() && form.accountHolder.trim() && form.mandateConsent && form.privacyConsent) &&
    (!minor || Boolean(form.guardianName.trim() && form.guardianPhone.trim()));

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const {
        data,
        error: problem,
        response,
      } = await api.POST('/api/v1/membership-applications', {
        body: {
          firstName: form.firstName,
          namePrefix: form.namePrefix || null,
          lastName: form.lastName,
          gender: form.gender || null,
          birthDate: birthDate!,
          addressLine: form.addressLine,
          postalCode: form.postalCode,
          city: form.city,
          email: form.email.trim(),
          phone: form.phone || null,
          guardianName: minor ? form.guardianName : null,
          guardianPhone: minor ? form.guardianPhone : null,
          iban: form.iban,
          accountHolder: form.accountHolder,
          mandateConsent: form.mandateConsent,
          privacyConsent: form.privacyConsent,
          photoConsent: form.photoConsent,
          source: 'App',
        },
      });
      if (data) {
        setApplicationId(data.id);
        setStep('code');
      } else {
        setError(problemText(response.status, problem));
      }
    } catch {
      setError('Geen verbinding. Probeer het opnieuw.');
    } finally {
      setBusy(false);
    }
  }

  async function verify() {
    setBusy(true);
    setError(null);
    try {
      const { error: problem, response } = await api.POST('/api/v1/membership-applications/{id}/verify-email', {
        params: { path: { id: applicationId! } },
        body: { code: code.trim() },
      });
      if (response.ok) {
        setStep('done');
      } else {
        setError(problemText(response.status, problem));
      }
    } catch {
      setError('Geen verbinding. Probeer het opnieuw.');
    } finally {
      setBusy(false);
    }
  }

  async function resend() {
    setError(null);
    const { response } = await api
      .POST('/api/v1/membership-applications/{id}/resend-code', { params: { path: { id: applicationId! } } })
      .catch(() => ({ response: null }));
    setError(response?.ok ? 'Er is een nieuwe code verstuurd.' : 'Een nieuwe code sturen lukt nu niet.');
  }

  const errorText = error ? (
    <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
      {error}
    </AppText>
  ) : null;

  return (
    <Screen>
      <BackLink label="Lid worden" />
      <LargeTitleHeader title="Aanmelden" />
      <View style={styles.content}>
        {step === 'done' ? (
          <Card style={styles.card}>
            <AppText variant="sectionHeader" accessibilityRole="header">
              Bedankt voor je aanmelding!
            </AppText>
            <AppText variant="body" color={colors.textSecondary}>
              Je aanmelding is ontvangen. Het bestuur beoordeelt hem zo snel mogelijk; daarna krijg je een e-mail.
            </AppText>
          </Card>
        ) : step === 'code' ? (
          <Card style={styles.card}>
            <AppText variant="sectionHeader" accessibilityRole="header">
              Bevestig je e-mailadres
            </AppText>
            <AppText variant="body" color={colors.textSecondary}>
              We hebben een code van 6 cijfers gestuurd naar {form.email.trim()}. Kijk ook in je map met ongewenste
              e-mail.
            </AppText>
            <TextField
              label="Code"
              value={code}
              onChangeText={setCode}
              keyboardType="number-pad"
              maxLength={6}
              autoComplete="one-time-code"
            />
            {errorText}
            <Button
              label={busy ? 'Even geduld…' : 'Bevestigen'}
              onPress={verify}
              disabled={!/^\d{6}$/.test(code.trim()) || busy}
            />
            <Button label="Nieuwe code sturen" variant="secondary" onPress={resend} />
          </Card>
        ) : (
          <>
            <Card style={styles.card}>
              <AppText variant="sectionHeader" accessibilityRole="header">
                Het nieuwe lid
              </AppText>
              <TextField
                label="Voornaam"
                value={form.firstName}
                onChangeText={set('firstName')}
                autoComplete="given-name"
                maxLength={50}
              />
              <TextField
                label="Tussenvoegsel"
                value={form.namePrefix}
                onChangeText={set('namePrefix')}
                maxLength={20}
              />
              <TextField
                label="Achternaam"
                value={form.lastName}
                onChangeText={set('lastName')}
                autoComplete="family-name"
                maxLength={60}
              />
              <TextField
                label="Geboortedatum"
                hint="Bijvoorbeeld 21-03-2015"
                value={form.birthDate}
                onChangeText={set('birthDate')}
                keyboardType="numbers-and-punctuation"
                maxLength={10}
              />
              <View style={styles.choices} accessibilityRole="radiogroup" accessibilityLabel="Geslacht">
                {(
                  [
                    ['', 'Zeg ik liever niet'],
                    ['v', 'Vrouw'],
                    ['m', 'Man'],
                  ] as const
                ).map(([value, label]) => (
                  <Pressable
                    key={value}
                    onPress={() => set('gender')(value)}
                    accessibilityRole="radio"
                    accessibilityState={{ checked: form.gender === value }}
                    style={[styles.choice, { borderColor: form.gender === value ? colors.linkText : colors.border }]}
                  >
                    <AppText variant="caption" color={form.gender === value ? colors.linkText : colors.textPrimary}>
                      {label}
                    </AppText>
                  </Pressable>
                ))}
              </View>
              <TextField
                label="Straat en huisnummer"
                value={form.addressLine}
                onChangeText={set('addressLine')}
                autoComplete="street-address"
                maxLength={150}
              />
              <TextField
                label="Postcode"
                value={form.postalCode}
                onChangeText={set('postalCode')}
                autoCapitalize="characters"
                autoComplete="postal-code"
                maxLength={10}
              />
              <TextField label="Woonplaats" value={form.city} onChangeText={set('city')} maxLength={50} />
            </Card>

            {minor ? (
              <Card style={styles.card}>
                <AppText variant="sectionHeader" accessibilityRole="header">
                  Ouder of verzorger
                </AppText>
                <AppText variant="caption" color={colors.textSecondary}>
                  Het nieuwe lid is jonger dan 16. De ouder of verzorger krijgt het account voor de app en ontvangt de
                  e-mails.
                </AppText>
                <TextField
                  label="Naam ouder/verzorger"
                  value={form.guardianName}
                  onChangeText={set('guardianName')}
                  autoComplete="name"
                  maxLength={100}
                />
                <TextField
                  label="Telefoon ouder/verzorger"
                  value={form.guardianPhone}
                  onChangeText={set('guardianPhone')}
                  keyboardType="phone-pad"
                  maxLength={30}
                />
              </Card>
            ) : null}

            <Card style={styles.card}>
              <AppText variant="sectionHeader" accessibilityRole="header">
                Contact
              </AppText>
              <TextField
                label={minor ? 'E-mailadres ouder/verzorger' : 'E-mailadres'}
                value={form.email}
                onChangeText={set('email')}
                keyboardType="email-address"
                autoCapitalize="none"
                autoComplete="email"
                autoCorrect={false}
                maxLength={150}
              />
              {minor ? null : (
                <TextField
                  label="Telefoon"
                  value={form.phone}
                  onChangeText={set('phone')}
                  keyboardType="phone-pad"
                  maxLength={30}
                />
              )}
            </Card>

            <Card style={styles.card}>
              <AppText variant="sectionHeader" accessibilityRole="header">
                Contributie
              </AppText>
              <TextField
                label="IBAN"
                value={form.iban}
                onChangeText={set('iban')}
                autoCapitalize="characters"
                autoCorrect={false}
                maxLength={40}
              />
              <TextField
                label="Naam rekeninghouder"
                value={form.accountHolder}
                onChangeText={set('accountHolder')}
                maxLength={100}
              />
              <CheckboxRow label={mandaatTekst} checked={form.mandateConsent} onChange={set('mandateConsent')} />
            </Card>

            <Card style={styles.card}>
              <AppText variant="sectionHeader" accessibilityRole="header">
                Toestemming
              </AppText>
              <CheckboxRow label={privacyTekst} checked={form.privacyConsent} onChange={set('privacyConsent')} />
              <CheckboxRow label={fotoTekst} checked={form.photoConsent} onChange={set('photoConsent')} />
            </Card>

            {errorText}
            <Button
              label={busy ? 'Even geduld…' : 'Aanmelding versturen'}
              onPress={submit}
              disabled={!complete || busy}
            />
          </>
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16 },
  card: { padding: 16, gap: 14 },
  choices: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  choice: { borderWidth: 1.5, borderRadius: 999, paddingHorizontal: 14, paddingVertical: 8 },
});
