import { useQueryClient } from '@tanstack/react-query';
import * as DocumentPicker from 'expo-document-picker';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, Alert, Pressable, StyleSheet, View } from 'react-native';
import { api, apiBaseUrl } from '../../api/client';
import {
  queryKeys,
  useMyBuildLocations,
  useParade,
  useParadeCategories,
  useRegistrationDocuments,
} from '../../api/queries';
import { getAccessToken, getInstallationId } from '../../auth/session';
import {
  addressForm,
  categoryRule,
  emptyForm,
  formatAddress,
  formFromRegistration,
  lengthInput,
  problemFrom,
  sameAddress,
  stepFields,
  stepMissing,
  stepsFor,
  stepTitles,
  toRequest,
  type AddressForm,
  type ParadeCategory,
  type Problem,
  type Registration,
  type RegistrationForm,
  type StepKey,
  type ValidationIssue,
} from '../../features/parade';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, CheckboxRow, ErrorState, Screen, TextField } from '../../ui';

type Phase = 'form' | 'code' | 'done';

/**
 * Groep inschrijven voor de optocht (fase 11b, Figma 🏁 Optocht-wizard). Leden met het recht Groepsverantwoordelijke
 * werken in een concept dat bij elke stap wordt bewaard (ook verder op een ander toestel); groepsnaam en bouwlocatie zijn
 * vooringevuld. Gasten (`?gast=1`) vullen alles leeg in, bevestigen hun e-mailadres met een code en krijgen dan pas een
 * opgavenummer. Elke inschrijving wordt daarna beoordeeld door de optochtcommissie.
 */
export default function InschrijvenScreen() {
  const params = useLocalSearchParams<{ id?: string; gast?: string; stap?: string }>();
  const guest = params.gast === '1';
  const { colors } = useTheme();
  const queryClient = useQueryClient();
  const parade = useParade();
  const categories = useParadeCategories();
  const locations = useMyBuildLocations(!guest);

  const [registration, setRegistration] = useState<Registration | null>(null);
  const [form, setForm] = useState<RegistrationForm>(emptyForm);
  const [stepIndex, setStepIndex] = useState(Number(params.stap ?? 0));
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [problem, setProblem] = useState<Problem | null>(null);
  const [warnings, setWarnings] = useState<ValidationIssue[]>([]);
  const [rulesAccepted, setRulesAccepted] = useState(false);
  const [phase, setPhase] = useState<Phase>('form');
  const [guestId, setGuestId] = useState<string | null>(null);
  const [code, setCode] = useState('');
  const [number, setNumber] = useState<number | null>(null);
  const [loadError, setLoadError] = useState(false);
  const [supplemented, setSupplemented] = useState(false);
  const started = useRef(false);

  const steps = stepsFor(guest);
  const step = steps[Math.min(stepIndex, steps.length - 1)]!;
  const draft = !registration || registration.status === 'Draft';
  // De commissie vroeg om een aanvulling: alles is te wijzigen en de groep dient de aanvulling opnieuw in.
  const supplement = registration?.status === 'AdditionalInformationRequired';
  const subjectRequired = parade.data?.subjectRequired ?? true;
  const set = <K extends keyof RegistrationForm>(key: K, value: RegistrationForm[K]) => {
    setForm((current) => ({ ...current, [key]: value }));
    setSaved(false);
  };

  // Lid: bestaand concept openen of een nieuw (vooringevuld) concept maken.
  useEffect(() => {
    if (guest || started.current) return;
    started.current = true;
    (async () => {
      const result = params.id
        ? await api.GET('/api/v1/parade/registrations/{id}', { params: { path: { id: params.id } } })
        : await api.POST('/api/v1/parade/registrations');
      if (result.data) {
        setRegistration(result.data);
        setForm(formFromRegistration(result.data));
        setWarnings(result.data.warnings);
        if (!params.id) queryClient.invalidateQueries({ queryKey: queryKeys.myRegistrations });
      } else {
        setLoadError(true);
        setProblem(problemFrom(result.response.status, result.error));
      }
    })().catch(() => setLoadError(true));
  }, [guest, params.id, queryClient]);

  async function save(): Promise<boolean> {
    if (guest || !registration) return true;
    const { data, error, response } = await api.PUT('/api/v1/parade/registrations/{id}', {
      params: { path: { id: registration.id } },
      body: toRequest(form, registration.version),
    });
    if (!data) {
      setProblem(problemFrom(response.status, error));
      return false;
    }
    setRegistration(data);
    setWarnings(data.warnings);
    setSaved(true);
    queryClient.setQueryData(queryKeys.myRegistration(data.id), data);
    return true;
  }

  async function next() {
    setProblem(null);
    const missing = stepMissing(step, form, subjectRequired);
    if (missing) {
      setProblem({ message: missing, issues: [] });
      return;
    }
    setBusy(true);
    try {
      if (await save()) setStepIndex((i) => Math.min(i + 1, steps.length - 1));
    } catch {
      setProblem({
        message: 'Geen verbinding. Je gegevens zijn nog niet opgeslagen; probeer het opnieuw.',
        issues: [],
      });
    } finally {
      setBusy(false);
    }
  }

  async function back() {
    setProblem(null);
    if (stepIndex === 0) return router.back();
    setBusy(true);
    await save().catch(() => false);
    setBusy(false);
    setStepIndex((i) => i - 1);
  }

  async function submit() {
    setProblem(null);
    setBusy(true);
    try {
      if (guest) {
        const { data, error, response } = await api.POST('/api/v1/parade/public-registrations', {
          body: { registration: toRequest(form, null), rulesAccepted },
        });
        if (!data) return setProblem(problemFrom(response.status, error));
        setGuestId(data.id);
        setPhase('code');
        return;
      }
      if (!(await save()) || !registration) return;
      if (!draft && !supplement) {
        // Een ingediende inschrijving aanpassen: opslaan is genoeg (de commissie ziet de wijzigingen).
        queryClient.invalidateQueries({ queryKey: queryKeys.myRegistrations });
        return router.back();
      }
      const { data, error, response } = await api.POST('/api/v1/parade/registrations/{id}/submit', {
        params: { path: { id: registration.id } },
      });
      if (!data) return setProblem(problemFrom(response.status, error));
      setSupplemented(supplement);
      setRegistration(data);
      setNumber(data.registrationNumber ?? null);
      setPhase('done');
      queryClient.invalidateQueries({ queryKey: queryKeys.myRegistrations });
      queryClient.invalidateQueries({ queryKey: queryKeys.myBuildLocations });
    } catch {
      setProblem({ message: 'Geen verbinding. Probeer het opnieuw.', issues: [] });
    } finally {
      setBusy(false);
    }
  }

  async function verify() {
    setProblem(null);
    setBusy(true);
    try {
      const { data, error, response } = await api.POST('/api/v1/parade/public-registrations/{id}/verify-email', {
        params: { path: { id: guestId! } },
        body: { code: code.trim() },
      });
      if (!data) return setProblem(problemFrom(response.status, error));
      setNumber(data.registrationNumber);
      setPhase('done');
    } catch {
      setProblem({ message: 'Geen verbinding. Probeer het opnieuw.', issues: [] });
    } finally {
      setBusy(false);
    }
  }

  async function resend() {
    const { response } = await api
      .POST('/api/v1/parade/public-registrations/{id}/resend-code', { params: { path: { id: guestId! } } })
      .catch(() => ({ response: null }));
    setProblem({
      message: response?.ok ? 'Er is een nieuwe code verstuurd.' : 'Een nieuwe code sturen lukt nu niet.',
      issues: [],
    });
  }

  const title = guest ? 'Optocht' : 'Groep inschrijven';

  if (loadError) {
    return (
      <Screen>
        <BackLink label="Optocht" />
        <ErrorState
          title="Inschrijven lukt nu niet"
          message={problem?.message}
          action={{ label: 'Terug', onPress: () => router.back() }}
        />
      </Screen>
    );
  }
  if (!guest && !registration) {
    return (
      <Screen>
        <BackLink label="Optocht" />
        <ActivityIndicator style={styles.loading} accessibilityLabel="Laden" />
      </Screen>
    );
  }

  if (phase === 'done') {
    return (
      <Screen>
        <View style={styles.content}>
          <Card style={styles.card}>
            <AppText variant="largeTitle" accessibilityRole="header">
              {supplemented ? 'Aanvulling ingediend' : 'Ingeschreven!'}
            </AppText>
            <AppText variant="body" color={colors.textSecondary}>
              {supplemented
                ? `De optochtcommissie beoordeelt de inschrijving van ${form.groupName} opnieuw. Je krijgt bericht zodra dat is gebeurd.`
                : `${form.groupName} is aangemeld voor de ${parade.data?.name ?? 'optocht'}.`}
            </AppText>
            <AppText variant="label" color={colors.textSecondary}>
              JULLIE OPGAVENUMMER
            </AppText>
            <AppText variant="largeTitle" color={colors.accentText} style={styles.number}>
              {number ?? '–'}
            </AppText>
            <AppText variant="body" color={colors.textSecondary}>
              De optochtcommissie beoordeelt jullie inschrijving; pas na goedkeuring is hij definitief. Het opgavenummer
              is de volgorde van binnenkomst, niet jullie startnummer.
            </AppText>
            <AppText variant="caption" color={colors.textSecondary}>
              {guest
                ? 'Je ontvangt een bevestiging per e-mail, met een link om de status te bekijken.'
                : 'Je ontvangt de bevestiging ook per e-mail en in je meldingen.'}
            </AppText>
          </Card>
          {guest ? (
            <Button label="Terug naar de optocht" onPress={() => router.back()} />
          ) : (
            <>
              <Button label="Naar mijn inschrijving" onPress={() => router.replace(`/optocht/${registration!.id}`)} />
            </>
          )}
        </View>
      </Screen>
    );
  }

  if (phase === 'code') {
    return (
      <Screen>
        <BackLink label="Optocht" />
        <View style={styles.content}>
          <Card style={styles.card}>
            <AppText variant="sectionHeader" accessibilityRole="header">
              Bevestig je e-mailadres
            </AppText>
            <AppText variant="body" color={colors.textSecondary}>
              We hebben een code van 6 cijfers gestuurd naar {form.contactEmail.trim()}. Pas na deze code is de
              inschrijving ingediend.
            </AppText>
            <TextField
              label="Code"
              value={code}
              onChangeText={setCode}
              keyboardType="number-pad"
              maxLength={6}
              autoComplete="one-time-code"
            />
            <ProblemText problem={problem} />
            <Button
              label={busy ? 'Even geduld…' : 'Bevestigen'}
              onPress={verify}
              disabled={!/^\d{6}$/.test(code.trim()) || busy}
            />
            <Button label="Nieuwe code sturen" variant="secondary" onPress={resend} />
          </Card>
        </View>
      </Screen>
    );
  }

  const stepWarnings = warnings.filter((w) => stepFields[step].includes(w.field));

  return (
    <Screen>
      <View style={styles.header}>
        <BackLink label="Optocht" />
        {saved && !guest ? (
          <AppText variant="caption" color={colors.textSecondary} accessibilityLiveRegion="polite">
            Concept opgeslagen
          </AppText>
        ) : null}
      </View>
      <View style={styles.content}>
        <View style={styles.progress} accessibilityLabel={`Stap ${stepIndex + 1} van ${steps.length}`}>
          {steps.map((s, i) => (
            <View
              key={s}
              style={[styles.bar, { backgroundColor: i <= stepIndex ? colors.accentText : colors.border }]}
            />
          ))}
        </View>
        <AppText variant="label" color={colors.textSecondary}>
          STAP {stepIndex + 1} VAN {steps.length} · {title.toUpperCase()}
        </AppText>
        <AppText variant="largeTitle" accessibilityRole="header">
          {stepTitles[step]}
        </AppText>
        {supplement && registration?.reviewReason ? (
          <Card style={styles.card}>
            <AppText variant="bodyStrong">Gevraagd door de optochtcommissie</AppText>
            <AppText variant="body" color={colors.textSecondary}>
              {registration.reviewReason}
            </AppText>
          </Card>
        ) : null}

        {step === 'group' ? (
          <Card style={styles.card}>
            <AppText variant="body" color={colors.textSecondary}>
              Hoe heet jullie groep?{guest ? '' : ' Je concept wordt automatisch bewaard.'}
            </AppText>
            <TextField
              label="Groepsnaam"
              hint="Zoals de groep in het programma en bij de jury komt te staan."
              value={form.groupName}
              onChangeText={(v) => set('groupName', v)}
              maxLength={100}
            />
            {guest ? (
              <AppText variant="caption" color={colors.textSecondary}>
                Lid van de vereniging? Log in: dan zijn je gegevens al ingevuld en kun je later verder waar je was.
              </AppText>
            ) : (
              <AppText variant="caption" color={colors.textSecondary}>
                Je kunt tussendoor stoppen: je gaat later verder waar je was, ook op een ander toestel.
              </AppText>
            )}
          </Card>
        ) : null}

        {step === 'contact' ? (
          <Card style={styles.card}>
            <AppText variant="body" color={colors.textSecondary}>
              Wie kan de optochtcommissie bereiken?{guest ? '' : ' Vooringevuld met je eigen gegevens.'}
            </AppText>
            <TextField
              label="Naam"
              value={form.contactName}
              onChangeText={(v) => set('contactName', v)}
              autoComplete="name"
              maxLength={100}
            />
            <TextField
              label="Telefoon"
              hint="Alleen voor de optocht, niet zichtbaar voor anderen."
              value={form.contactPhone}
              onChangeText={(v) => set('contactPhone', v)}
              keyboardType="phone-pad"
              maxLength={30}
            />
            <TextField
              label="E-mailadres"
              hint="Hier sturen we de bevestiging en het startnummer naartoe."
              value={form.contactEmail}
              onChangeText={(v) => set('contactEmail', v)}
              keyboardType="email-address"
              autoCapitalize="none"
              autoCorrect={false}
              autoComplete="email"
              maxLength={254}
            />
          </Card>
        ) : null}

        {step === 'category' ? (
          <CategoryStep
            form={form}
            categories={categories.data ?? []}
            onCategory={(id) => set('categoryId', id)}
            onCount={set}
            onMusic={(v) => set('hasMusic', v)}
          />
        ) : null}

        {step === 'subject' ? (
          <Card style={styles.card}>
            <AppText variant="body" color={colors.textSecondary}>
              Waar gaat jullie groep over? De jury en de omroeper gebruiken dit.
            </AppText>
            <TextField
              label={subjectRequired ? 'Onderwerp' : 'Onderwerp (optioneel)'}
              hint={subjectRequired ? 'Verplicht, maximaal 150 tekens.' : 'Maximaal 150 tekens.'}
              value={form.subject}
              onChangeText={(v) => set('subject', v)}
              maxLength={150}
            />
            <TextField
              label="Toelichting (optioneel)"
              hint="Maximaal 2000 tekens."
              value={form.subjectDescription}
              onChangeText={(v) => set('subjectDescription', v)}
              multiline
              maxLength={2000}
            />
          </Card>
        ) : null}

        {step === 'location' ? (
          <LocationStep
            form={form}
            guest={guest}
            saved={locations.data ?? []}
            onBuild={(a) => set('build', a)}
            onJurySame={(v) => set('jurySame', v)}
            onJury={(a) => set('jury', a)}
            onDeleted={() => queryClient.invalidateQueries({ queryKey: queryKeys.myBuildLocations })}
          />
        ) : null}

        {step === 'length' ? (
          <Card style={styles.card}>
            <AppText variant="body" color={colors.textSecondary}>
              Met de lengte stelt de commissie de optocht samen.
            </AppText>
            <TextField
              label="Geschatte lengte (meter)"
              hint="Inclusief trekkend voertuig en eventuele aanhanger. Maximaal 100 m, 1 decimaal (bijv. 12,5)."
              value={form.estimatedLength}
              onChangeText={(v) => set('estimatedLength', lengthInput(v))}
              keyboardType="decimal-pad"
              maxLength={6}
            />
            <TextField
              label="Extra informatie (optioneel)"
              hint="Bijvoorbeeld muziek, geluid of bijzonderheden voor de veiligheid."
              value={form.additionalInformation}
              onChangeText={(v) => set('additionalInformation', v)}
              multiline
              maxLength={4000}
            />
          </Card>
        ) : null}

        {step === 'documents' && registration ? (
          <DocumentsStep registrationId={registration.id} maxSizeMb={parade.data?.maxDocumentSizeMb ?? 10} />
        ) : null}

        {step === 'review' ? (
          <ReviewStep
            form={form}
            steps={steps}
            categories={categories.data ?? []}
            onEdit={(s) => setStepIndex(steps.indexOf(s))}
            rulesAccepted={rulesAccepted}
            onRules={setRulesAccepted}
            draft={draft}
            supplement={supplement}
          />
        ) : null}

        {stepWarnings.map((w) => (
          <AppText key={w.field + w.message} variant="caption" color={colors.textSecondary}>
            ⚠︎ {w.message}
          </AppText>
        ))}
        <ProblemText problem={problem} />

        <View style={styles.buttons}>
          {stepIndex > 0 ? (
            <View style={styles.flex}>
              <Button label="Vorige" variant="secondary" onPress={back} disabled={busy} />
            </View>
          ) : null}
          <View style={styles.flex}>
            {step === 'review' ? (
              <Button
                label={
                  busy
                    ? 'Even geduld…'
                    : draft
                      ? 'Inschrijving indienen'
                      : supplement
                        ? 'Aanvulling indienen'
                        : 'Wijzigingen opslaan'
                }
                onPress={submit}
                disabled={busy || (draft && !rulesAccepted)}
              />
            ) : (
              <Button label={busy ? 'Opslaan…' : 'Volgende'} onPress={next} disabled={busy} />
            )}
          </View>
        </View>
      </View>
    </Screen>
  );
}

function ProblemText({ problem }: { problem: Problem | null }) {
  const { colors } = useTheme();
  if (!problem) return null;
  return (
    <View accessibilityRole="alert" style={styles.problem}>
      <AppText variant="body" color={colors.accentText}>
        {problem.message}
      </AppText>
      {problem.issues.map((i) => (
        <AppText key={i.field + i.message} variant="caption" color={colors.accentText}>
          • {i.message}
        </AppText>
      ))}
    </View>
  );
}

function Counter({
  label,
  hint,
  value,
  onChange,
}: {
  label: string;
  hint: string;
  value: number;
  onChange: (v: number) => void;
}) {
  const { colors } = useTheme();
  return (
    <View style={styles.counter}>
      <View style={styles.flex}>
        <AppText variant="bodyStrong">{label}</AppText>
        <AppText variant="caption" color={colors.textSecondary}>
          {hint}
        </AppText>
      </View>
      <Pressable
        onPress={() => onChange(Math.max(0, value - 1))}
        accessibilityRole="button"
        accessibilityLabel={`${label} min 1`}
        style={[styles.round, { borderColor: colors.border }]}
        hitSlop={6}
      >
        <AppText variant="sectionHeader">−</AppText>
      </Pressable>
      <AppText variant="sectionHeader" style={styles.count} accessibilityLabel={`${label}: ${value}`}>
        {value}
      </AppText>
      <Pressable
        onPress={() => onChange(Math.min(1000, value + 1))}
        accessibilityRole="button"
        accessibilityLabel={`${label} plus 1`}
        style={[styles.round, { borderColor: colors.border }]}
        hitSlop={6}
      >
        <AppText variant="sectionHeader">+</AppText>
      </Pressable>
    </View>
  );
}

function CategoryStep({
  form,
  categories,
  onCategory,
  onCount,
  onMusic,
}: {
  form: RegistrationForm;
  categories: ParadeCategory[];
  onCategory: (id: number) => void;
  onCount: (key: 'adultCount' | 'childrenCount', value: number) => void;
  onMusic: (value: boolean) => void;
}) {
  const { colors } = useTheme();
  const chosen = categories.find((c) => c.id === form.categoryId);
  const counted =
    chosen?.participantCountBasis === 'AdultsOnly'
      ? form.adultCount
      : chosen?.participantCountBasis === 'ChildrenOnly'
        ? form.childrenCount
        : form.adultCount + form.childrenCount;
  const tooFew = chosen?.minimumParticipants != null && counted < chosen.minimumParticipants;
  const tooMany = chosen?.maximumParticipants != null && counted > chosen.maximumParticipants;
  const groups = [
    { label: 'VOLWASSENEN', items: categories.filter((c) => c.ageGroup === 'Adult') },
    { label: 'JEUGD', items: categories.filter((c) => c.ageGroup === 'Youth') },
  ];
  return (
    <>
      <AppText variant="body" color={colors.textSecondary}>
        Alleen de doelgroep telt mee: volwassenen bij volwassenen, kinderen bij jeugd.
      </AppText>
      {groups
        .filter((g) => g.items.length)
        .map((g) => (
          <View key={g.label} style={styles.group} accessibilityRole="radiogroup" accessibilityLabel={g.label}>
            <AppText variant="label" color={colors.textSecondary}>
              {g.label}
            </AppText>
            {g.items.map((c) => {
              const selected = c.id === form.categoryId;
              return (
                <Pressable
                  key={c.id}
                  onPress={() => onCategory(c.id)}
                  accessibilityRole="radio"
                  accessibilityState={{ checked: selected }}
                  style={[
                    styles.option,
                    { borderColor: selected ? colors.linkText : colors.border, backgroundColor: colors.surface },
                  ]}
                >
                  <AppText variant="bodyStrong">{c.name}</AppText>
                  <AppText variant="caption" color={colors.textSecondary}>
                    {categoryRule(c)}
                  </AppText>
                </Pressable>
              );
            })}
          </View>
        ))}
      <AppText variant="label" color={colors.textSecondary}>
        AANTAL DEELNEMERS
      </AppText>
      <Card style={styles.card}>
        <Counter
          label="Volwassenen"
          hint={
            chosen?.participantCountBasis === 'ChildrenOnly'
              ? 'Tellen niet mee (begeleiding)'
              : 'Tellen mee in deze categorie'
          }
          value={form.adultCount}
          onChange={(v) => onCount('adultCount', v)}
        />
        <Counter
          label="Kinderen"
          hint={
            chosen?.participantCountBasis === 'AdultsOnly'
              ? 'Tellen niet mee (begeleiding)'
              : 'Tellen mee in deze categorie'
          }
          value={form.childrenCount}
          onChange={(v) => onCount('childrenCount', v)}
        />
      </Card>
      {chosen && (tooFew || tooMany) ? (
        <AppText variant="caption" color={colors.accentText} accessibilityLiveRegion="polite">
          Deze categorie is voor {categoryRule(chosen)} (nu {counted}).
        </AppText>
      ) : null}
      <AppText variant="label" color={colors.textSecondary}>
        MUZIEK
      </AppText>
      <View style={styles.row} accessibilityRole="radiogroup" accessibilityLabel="Hebben jullie muziek bij je?">
        {(
          [
            [true, 'Ja, met muziek'],
            [false, 'Nee, zonder muziek'],
          ] as const
        ).map(([value, label]) => {
          const selected = form.hasMusic === value;
          return (
            <Pressable
              key={label}
              onPress={() => onMusic(value)}
              accessibilityRole="radio"
              accessibilityState={{ checked: selected }}
              style={[
                styles.option,
                styles.flex,
                { borderColor: selected ? colors.linkText : colors.border, backgroundColor: colors.surface },
              ]}
            >
              <AppText variant="bodyStrong">{label}</AppText>
            </Pressable>
          );
        })}
      </View>
    </>
  );
}

function AddressFields({ value, onChange }: { value: AddressForm; onChange: (a: AddressForm) => void }) {
  const field = (key: keyof AddressForm) => (v: string) => onChange({ ...value, [key]: v });
  return (
    <>
      <View style={styles.row}>
        <View style={styles.flex}>
          <TextField
            label="Postcode"
            value={value.postalCode}
            onChangeText={field('postalCode')}
            autoCapitalize="characters"
            maxLength={10}
          />
        </View>
        <View style={styles.flex}>
          <TextField label="Huisnummer" value={value.houseNumber} onChangeText={field('houseNumber')} maxLength={10} />
        </View>
        <View style={styles.small}>
          <TextField label="Toev." value={value.addition} onChangeText={field('addition')} maxLength={10} />
        </View>
      </View>
      <TextField label="Straat" value={value.street} onChangeText={field('street')} maxLength={100} />
      <TextField label="Plaats" value={value.city} onChangeText={field('city')} maxLength={60} />
    </>
  );
}

function LocationStep({
  form,
  guest,
  saved,
  onBuild,
  onJurySame,
  onJury,
  onDeleted,
}: {
  form: RegistrationForm;
  guest: boolean;
  saved: { id: string; address: Registration['buildAddress'] }[];
  onBuild: (a: AddressForm) => void;
  onJurySame: (v: boolean) => void;
  onJury: (a: AddressForm) => void;
  onDeleted: () => void;
}) {
  const { colors } = useTheme();
  const options = useMemo(() => saved.map((l) => ({ id: l.id, address: addressForm(l.address) })), [saved]);
  const matching = options.find((o) => sameAddress(o.address, form.build));
  const [adding, setAdding] = useState(!matching && Boolean(form.build.postalCode));

  const remove = (id: string, label: string) =>
    Alert.alert('Locatie verwijderen', `${label} verwijderen uit je bewaarde locaties?`, [
      { text: 'Annuleren', style: 'cancel' },
      {
        text: 'Verwijderen',
        style: 'destructive',
        onPress: async () => {
          await api
            .DELETE('/api/v1/parade/build-locations/{locationId}', { params: { path: { locationId: id } } })
            .catch(() => null);
          onDeleted();
        },
      },
    ]);

  return (
    <>
      <AppText variant="body" color={colors.textSecondary}>
        Waar wordt de wagen of het kostuum gebouwd?
      </AppText>
      {!guest && options.length > 0 ? (
        <View style={styles.group} accessibilityRole="radiogroup" accessibilityLabel="Bouwlocatie">
          {options.map((o) => {
            const selected = !adding && matching?.id === o.id;
            const label = formatAddress(o.address);
            return (
              <View
                key={o.id}
                style={[
                  styles.option,
                  styles.locationRow,
                  { borderColor: selected ? colors.linkText : colors.border, backgroundColor: colors.surface },
                ]}
              >
                <Pressable
                  style={styles.flex}
                  onPress={() => {
                    setAdding(false);
                    onBuild(o.address);
                  }}
                  accessibilityRole="radio"
                  accessibilityState={{ checked: selected }}
                  accessibilityLabel={`Zelfde locatie: ${label}`}
                >
                  <AppText variant="bodyStrong">Zelfde locatie</AppText>
                  <AppText variant="caption" color={colors.textSecondary}>
                    {label}
                  </AppText>
                </Pressable>
                <Pressable
                  onPress={() => remove(o.id, label)}
                  accessibilityRole="button"
                  accessibilityLabel={`Verwijder ${label}`}
                  hitSlop={8}
                >
                  <AppText variant="caption" color={colors.accentText}>
                    Verwijderen
                  </AppText>
                </Pressable>
              </View>
            );
          })}
          <Pressable
            onPress={() => {
              setAdding(true);
              onBuild(addressForm(null));
            }}
            accessibilityRole="radio"
            accessibilityState={{ checked: adding }}
            style={[
              styles.option,
              { borderColor: adding ? colors.linkText : colors.border, backgroundColor: colors.surface },
            ]}
          >
            <AppText variant="bodyStrong">+ Andere locatie</AppText>
          </Pressable>
        </View>
      ) : null}
      {guest || options.length === 0 || adding ? (
        <Card style={styles.card}>
          <AddressFields value={form.build} onChange={onBuild} />
        </Card>
      ) : null}
      <Card style={styles.card}>
        <CheckboxRow
          label="Stalling voor de jury is gelijk aan het bouwadres"
          checked={form.jurySame}
          onChange={onJurySame}
        />
        {form.jurySame ? (
          <AppText variant="caption" color={colors.textSecondary}>
            Staat de wagen bij de jurering ergens anders? Zet het vinkje uit, dan vragen we dat adres.
          </AppText>
        ) : (
          <AddressFields value={form.jury} onChange={onJury} />
        )}
      </Card>
    </>
  );
}

function DocumentsStep({ registrationId, maxSizeMb }: { registrationId: string; maxSizeMb: number }) {
  const { colors } = useTheme();
  const queryClient = useQueryClient();
  const documents = useRegistrationDocuments(registrationId);
  const [error, setError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.myRegistrationDocuments(registrationId) });

  async function add() {
    setError(null);
    const picked = await DocumentPicker.getDocumentAsync({
      type: ['application/pdf', 'image/jpeg', 'image/png'],
      copyToCacheDirectory: true,
    });
    const file = picked.canceled ? null : picked.assets[0];
    if (!file) return;
    if (file.size && file.size > maxSizeMb * 1024 * 1024) {
      setError(`Dit bestand is groter dan ${maxSizeMb} MB.`);
      return;
    }
    setUploading(true);
    try {
      // Multipart met een bestand van het toestel: gewone fetch, want openapi-fetch kent de React Native-bestandsvorm niet.
      const body = new FormData();
      body.append('type', 'Other');
      body.append('file', {
        uri: file.uri,
        name: file.name,
        type: file.mimeType ?? 'application/octet-stream',
      } as unknown as Blob);
      const token = await getAccessToken();
      const response = await fetch(`${apiBaseUrl}/api/v1/parade/registrations/${registrationId}/documents`, {
        method: 'POST',
        headers: { authorization: `Bearer ${token ?? ''}`, 'x-device-id': await getInstallationId() },
        body,
      });
      if (!response.ok) {
        const problem = problemFrom(response.status, await response.json().catch(() => undefined));
        setError(problem.message);
      }
      refresh();
    } catch {
      setError('Uploaden lukt nu niet. Probeer het opnieuw.');
    } finally {
      setUploading(false);
    }
  }

  const remove = async (id: string) => {
    await api
      .DELETE('/api/v1/parade/registrations/{id}/documents/{documentId}', {
        params: { path: { id: registrationId, documentId: id } },
      })
      .catch(() => null);
    refresh();
  };

  return (
    <>
      <AppText variant="body" color={colors.textSecondary}>
        Optioneel: bijvoorbeeld een verzekeringsbewijs of een tekening van de wagen. PDF, JPG of PNG, maximaal{' '}
        {maxSizeMb} MB.
      </AppText>
      <Card style={styles.card}>
        {(documents.data ?? []).map((d) => (
          <View key={d.id} style={styles.locationRow}>
            <View style={styles.flex}>
              <AppText variant="bodyStrong">{d.fileName}</AppText>
              <AppText variant="caption" color={colors.textSecondary}>
                {Math.max(1, Math.round(d.sizeBytes / 1024))} kB
              </AppText>
            </View>
            <Pressable
              onPress={() => remove(d.id)}
              accessibilityRole="button"
              accessibilityLabel={`Verwijder ${d.fileName}`}
              hitSlop={8}
            >
              <AppText variant="caption" color={colors.accentText}>
                Verwijderen
              </AppText>
            </Pressable>
          </View>
        ))}
        <Button
          label={uploading ? 'Uploaden…' : '+ Document toevoegen'}
          variant="secondary"
          onPress={add}
          disabled={uploading}
        />
        {error ? (
          <AppText variant="caption" color={colors.accentText} accessibilityRole="alert">
            {error}
          </AppText>
        ) : null}
      </Card>
      <AppText variant="caption" color={colors.textSecondary}>
        Documenten zijn alleen zichtbaar voor jullie beheerders en de optochtcommissie.
      </AppText>
    </>
  );
}

function ReviewStep({
  form,
  steps,
  categories,
  onEdit,
  rulesAccepted,
  onRules,
  draft,
  supplement,
}: {
  form: RegistrationForm;
  steps: StepKey[];
  categories: ParadeCategory[];
  onEdit: (s: StepKey) => void;
  rulesAccepted: boolean;
  onRules: (v: boolean) => void;
  draft: boolean;
  supplement: boolean;
}) {
  const { colors } = useTheme();
  const category = categories.find((c) => c.id === form.categoryId);
  const rows: { step: StepKey; label: string; value: string }[] = [
    { step: 'group', label: 'Groep', value: form.groupName },
    {
      step: 'contact',
      label: 'Contactpersoon',
      value: [form.contactName, form.contactPhone, form.contactEmail].filter(Boolean).join(' · '),
    },
    {
      step: 'category',
      label: 'Categorie en deelnemers',
      value: `${category?.name ?? '–'} · ${form.adultCount} volwassenen, ${form.childrenCount} kinderen · ${
        form.hasMusic === null ? 'muziek niet opgegeven' : form.hasMusic ? 'met muziek' : 'zonder muziek'
      }`,
    },
    { step: 'subject', label: 'Onderwerp', value: form.subject || '–' },
    {
      step: 'location',
      label: 'Bouwlocatie',
      value: `${formatAddress(form.build)} · ${form.jurySame ? 'jury op hetzelfde adres' : `jury: ${formatAddress(form.jury)}`}`,
    },
    { step: 'length', label: 'Lengte', value: form.estimatedLength ? `${form.estimatedLength} m` : 'niet opgegeven' },
  ];
  return (
    <>
      <AppText variant="body" color={colors.textSecondary}>
        {draft
          ? 'Klopt alles? Na het indienen krijgen jullie een opgavenummer; daarna beoordeelt de optochtcommissie de inschrijving.'
          : supplement
            ? 'Klopt alles? Na het indienen van de aanvulling beoordeelt de optochtcommissie de inschrijving opnieuw.'
            : 'Controleer je wijzigingen en sla ze op.'}
      </AppText>
      <Card style={styles.card}>
        {rows
          .filter((r) => steps.includes(r.step))
          .map((r) => (
            <View key={r.step} style={styles.locationRow}>
              <View style={styles.flex}>
                <AppText variant="caption" color={colors.textSecondary}>
                  {r.label}
                </AppText>
                <AppText variant="body">{r.value}</AppText>
              </View>
              <Pressable
                onPress={() => onEdit(r.step)}
                accessibilityRole="button"
                accessibilityLabel={`Wijzig ${r.label}`}
                hitSlop={8}
              >
                <AppText variant="caption" color={colors.linkText}>
                  Wijzig
                </AppText>
              </Pressable>
            </View>
          ))}
      </Card>
      {draft ? (
        <CheckboxRow
          label="Ik bevestig dat de gegevens juist zijn en ga akkoord met het optochtreglement."
          checked={rulesAccepted}
          onChange={onRules}
        />
      ) : null}
    </>
  );
}

const styles = StyleSheet.create({
  header: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingRight: 20 },
  content: { paddingHorizontal: 20, gap: 14, paddingBottom: 24 },
  loading: { marginTop: 48 },
  card: { padding: 16, gap: 14 },
  progress: { flexDirection: 'row', gap: 4 },
  bar: { flex: 1, height: 4, borderRadius: 2 },
  buttons: { flexDirection: 'row', gap: 10, marginTop: 4 },
  flex: { flex: 1 },
  row: { flexDirection: 'row', gap: 10 },
  small: { width: 76 },
  group: { gap: 8 },
  option: { borderWidth: 1.5, borderRadius: 14, padding: 14, gap: 2 },
  locationRow: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  counter: { flexDirection: 'row', alignItems: 'center', gap: 10 },
  round: { width: 40, height: 40, borderRadius: 20, borderWidth: 1.5, alignItems: 'center', justifyContent: 'center' },
  count: { minWidth: 32, textAlign: 'center' },
  number: { fontSize: 48, lineHeight: 56 },
  problem: { gap: 4 },
});
