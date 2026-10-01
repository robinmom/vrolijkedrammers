import { brand, radius } from '@drammers/design-tokens';
import { useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useMemo, useRef, useState } from 'react';
import { ActivityIndicator, Alert, FlatList, Pressable, ScrollView, StyleSheet, useWindowDimensions, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { api, unwrap } from '../../api/client';
import { queryKeys, useJurySession, useMe } from '../../api/queries';
import { criteria, passComplete, passes, valueOf, type JudgingEntry } from '../../features/judging';
import { useJudging } from '../../features/useJudging';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, QueryState, ScoreSlider } from '../../ui';

type Page = { kind: 'entry'; entry: JudgingEntry } | { kind: 'end' };

/**
 * J2–J7 Beoordelen (fase 22b, Figma "⚖️ Jury"): van links naar rechts door de optocht swipen. Per wagen of groep het
 * startnummer, de groep en het motto, dan per voorbijtrekken (1, 2, 3) vier sliders. Standaard alleen de eigen
 * categorieën; met "Hele optocht" ook de rest (dat telt pas mee na akkoord). Aan het einde: jurering indienen.
 */
export default function BeoordelenScreen() {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  const { width } = useWindowDimensions();
  const params = useLocalSearchParams<{ start?: string; passage?: string; einde?: string }>();
  const me = useMe();
  const session = useJurySession(me.data?.permissions.includes('parade.judge') ?? false);
  const judging = useJudging(session.data);
  const client = useQueryClient();
  const [whole, setWhole] = useState(false);
  const [pass, setPass] = useState(Number(params.passage) || 1);
  const [chosen, setChosen] = useState<number | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const list = useRef<FlatList<Page>>(null);
  const s = session.data;
  const submitted = Boolean(s?.submittedAt);

  const entries = useMemo(() => (s?.entries ?? []).filter((e) => whole || e.assigned), [s, whole]);
  const pages: Page[] = useMemo(() => [...entries.map((entry) => ({ kind: 'entry' as const, entry })), { kind: 'end' as const }], [entries]);

  // Beginnen bij de inzending (of het einde) waar het startscherm naar verwees.
  const initialIndex = useMemo(() => {
    if (params.einde) return pages.length - 1;
    return Math.max(0, pages.findIndex((p) => p.kind === 'entry' && p.entry.registrationId === params.start));
  }, [pages, params.einde, params.start]);
  const index = chosen ?? initialIndex;
  const setIndex = setChosen;

  const goTo = (i: number) => {
    const target = Math.max(0, Math.min(pages.length - 1, i));
    setIndex(target);
    list.current?.scrollToIndex({ index: target, animated: true });
  };

  const toggleWhole = () => {
    if (whole) {
      setWhole(false);
      setIndex(0);
      list.current?.scrollToOffset({ offset: 0, animated: false });
      return;
    }
    Alert.alert(
      'Je gaat nu de hele optocht beoordelen',
      'Je ziet dan ook de wagens en groepen uit categorieën die niet aan jou zijn toegewezen. Scores daarvan tellen pas mee als het bestuur of de hoofdjury ze goedkeurt; die krijgen hier een melding van.',
      [
        { text: 'Alleen mijn categorieën', style: 'cancel' },
        { text: 'Ja, hele optocht beoordelen', onPress: () => setWhole(true) },
      ],
    );
  };

  async function submit() {
    if (!s) return;
    setMessage(null);
    setSubmitting(true);
    try {
      if (!(await judging.sync())) {
        setMessage('Geen verbinding: je scores staan veilig op je telefoon. Indienen kan zodra er weer internet is.');
        return;
      }
      await unwrap(api.POST('/api/v1/jury/parades/{paradeId}/submit', { params: { path: { paradeId: s.paradeId } } }));
      await client.invalidateQueries({ queryKey: queryKeys.jury });
    } catch {
      setMessage('Indienen lukt nu niet. Je scores staan veilig op je telefoon; probeer het zo opnieuw.');
    } finally {
      setSubmitting(false);
    }
  }

  const confirmSubmit = (count: number, missing: number) =>
    Alert.alert(
      'Jurering indienen?',
      `Je dient de beoordelingen in van ${count} ${count === 1 ? 'wagen of groep' : 'wagens en groepen'}. Daarna kun je ze niet meer wijzigen.${missing > 0 ? ` ${missing} ${missing === 1 ? 'passage is' : 'passages zijn'} niet ingevuld.` : ''}`,
      [
        { text: 'Annuleren', style: 'cancel' },
        { text: 'Ja, indienen', style: 'destructive', onPress: () => void submit() },
      ],
    );

  if (!s) {
    return (
      <View style={[styles.screen, { paddingTop: insets.top, backgroundColor: colors.canvas }]}>
        <BackLink label="Jureren" />
        <QueryState query={session} />
      </View>
    );
  }

  if (submitted) {
    return (
      <View style={[styles.screen, styles.center, { paddingTop: insets.top, backgroundColor: colors.canvas }]}>
        <View style={[styles.okCircle, { backgroundColor: colors.tintGreen }]}>
          <AppText variant="largeTitle" color={colors.successText}>
            ✓
          </AppText>
        </View>
        <AppText variant="heroTitle" style={styles.centerText} accessibilityRole="header">
          Bedankt, je jurering is ingediend
        </AppText>
        <AppText variant="body" color={colors.textSecondary} style={styles.centerText}>
          De uitslagcommissie maakt de uitslag bekend bij de prijsuitreiking.
        </AppText>
        <Button label="Terug naar de optocht" variant="secondary" onPress={() => router.replace('/optocht')} />
      </View>
    );
  }

  const assigned = s.entries.filter((e) => e.assigned);
  const scoredOutside = s.entries.filter((e) => !e.assigned && passes.some((p) => criteria.some((c) => valueOf(judging.scores, e.registrationId, p, c.key) !== null)));
  const missing = passes.flatMap((p) => assigned.filter((e) => !passComplete(judging.scores, e.registrationId, p)).map((e) => ({ pass: p, entry: e })));
  const current = pages[index];
  const neighbour = (i: number) => {
    const p = pages[i];
    return p?.kind === 'entry' ? (p.entry.startNumber != null ? `nr. ${p.entry.startNumber}` : p.entry.groupName) : p ? 'Einde' : null;
  };

  return (
    <View style={[styles.screen, { paddingTop: insets.top, backgroundColor: colors.canvas }]}>
      <View style={styles.header}>
        <BackLink label="Jureren" />
        <View style={styles.headerRight}>
          <AppText variant="caption" color={colors.textSecondary}>
            {current?.kind === 'entry' ? `${index + 1} van ${entries.length}` : 'Einde optocht'}
          </AppText>
          <Pressable
            onPress={toggleWhole}
            accessibilityRole="switch"
            accessibilityState={{ checked: whole }}
            accessibilityLabel="Hele optocht"
            style={[styles.chip, { backgroundColor: whole ? `${brand.yellow}59` : colors.tintBlue }]}
            hitSlop={6}
          >
            <AppText variant="label" color={whole ? brand.navy : colors.linkText}>
              {whole ? 'Hele optocht ✓' : 'Hele optocht'}
            </AppText>
          </Pressable>
        </View>
      </View>

      <FlatList
        ref={list}
        data={pages}
        horizontal
        pagingEnabled
        showsHorizontalScrollIndicator={false}
        keyExtractor={(p) => (p.kind === 'entry' ? p.entry.registrationId : 'einde')}
        getItemLayout={(_, i) => ({ length: width, offset: width * i, index: i })}
        initialScrollIndex={initialIndex}
        onMomentumScrollEnd={(e) => setIndex(Math.round(e.nativeEvent.contentOffset.x / width))}
        renderItem={({ item }) =>
          item.kind === 'entry' ? (
            <ScrollView style={{ width }} contentContainerStyle={styles.page}>
              {!item.entry.assigned ? (
                <View style={[styles.banner, { backgroundColor: `${brand.yellow}40` }]}>
                  <AppText variant="caption" color={brand.navy}>
                    Niet aan jou toegewezen · telt pas mee na goedkeuring door het bestuur of de hoofdjury
                  </AppText>
                </View>
              ) : null}
              <View style={styles.entryHead}>
                <View style={[styles.number, { backgroundColor: brand.navy }]} accessible accessibilityLabel={`Startnummer ${item.entry.startNumber ?? 'onbekend'}`}>
                  <AppText variant="label" color="rgba(255,255,255,0.75)">
                    NR
                  </AppText>
                  <AppText variant="largeTitle" color="#FFFFFF" style={styles.numberText}>
                    {item.entry.startNumber ?? '–'}
                  </AppText>
                </View>
                <View style={styles.flex}>
                  <AppText variant="cardTitle" accessibilityRole="header">
                    {item.entry.groupName}
                  </AppText>
                  <View style={[styles.category, { backgroundColor: colors.tintBlue }]}>
                    <AppText variant="label" color={colors.linkText}>
                      {item.entry.categoryName}
                    </AppText>
                  </View>
                </View>
              </View>
              {item.entry.motto ? (
                <AppText variant="bodyStrong" color={colors.textSecondary}>
                  “{item.entry.motto}”
                </AppText>
              ) : null}
              <View style={[styles.segmented, { backgroundColor: colors.surfaceMuted }]} accessibilityRole="tablist">
                {passes.map((p) => {
                  const done = passComplete(judging.scores, item.entry.registrationId, p);
                  const active = p === pass;
                  return (
                    <Pressable
                      key={p}
                      onPress={() => setPass(p)}
                      accessibilityRole="tab"
                      accessibilityState={{ selected: active }}
                      accessibilityLabel={`Voorbijtrekken ${p}${done ? ', ingevuld' : ''}`}
                      style={[styles.segment, active && [styles.segmentActive, { backgroundColor: colors.surface }]]}
                    >
                      <AppText variant="link" color={active ? colors.textPrimary : done ? colors.successText : colors.textSecondary}>
                        Voorbij {p}
                        {done ? ' ✓' : ''}
                      </AppText>
                    </Pressable>
                  );
                })}
              </View>
              {pass > 1 && passComplete(judging.scores, item.entry.registrationId, pass - 1) ? (
                <AppText variant="caption" color={colors.textSecondary}>
                  Voorbij {pass - 1}:{' '}
                  {criteria.map((c) => `${c.label.toLowerCase()} ${valueOf(judging.scores, item.entry.registrationId, pass - 1, c.key)}`).join(' · ')}
                </AppText>
              ) : null}
              <Card style={styles.sliders}>
                {criteria.map((c) => (
                  <ScoreSlider
                    key={c.key}
                    label={c.label}
                    value={valueOf(judging.scores, item.entry.registrationId, pass, c.key)}
                    onChange={(v) => judging.setScore(item.entry.registrationId, pass, c.key, v)}
                  />
                ))}
              </Card>
            </ScrollView>
          ) : (
            <ScrollView style={{ width }} contentContainerStyle={styles.page}>
              <AppText variant="heroTitle" accessibilityRole="header">
                Klaar met jureren?
              </AppText>
              <AppText variant="body" color={colors.textSecondary}>
                Je bent aan het einde van de optocht. Controleer je beoordelingen en dien ze in. Daarna kun je niets meer wijzigen.
              </AppText>
              <Card style={styles.card}>
                <AppText variant="overline" color={colors.textSecondary}>
                  Overzicht
                </AppText>
                {passes.map((p) => {
                  const done = assigned.filter((e) => passComplete(judging.scores, e.registrationId, p)).length;
                  return (
                    <View key={p} style={styles.row}>
                      <AppText variant="bodyStrong" style={styles.flex}>
                        Voorbijtrekken {p}
                      </AppText>
                      <AppText variant="link" color={done === assigned.length ? colors.successText : colors.accentText}>
                        {done} van {assigned.length}
                        {done === assigned.length ? ' ✓' : ''}
                      </AppText>
                    </View>
                  );
                })}
              </Card>
              {missing.length > 0 ? (
                <View style={[styles.banner, { backgroundColor: `${brand.yellow}40` }]}>
                  <AppText variant="bodyStrong" color={brand.navy}>
                    Nog niet (helemaal) ingevuld
                  </AppText>
                  {passes
                    .filter((p) => missing.some((m) => m.pass === p))
                    .map((p) => (
                      <AppText key={p} variant="caption" color={brand.navy}>
                        Voorbij {p}:{' '}
                        {missing
                          .filter((m) => m.pass === p)
                          .slice(0, 6)
                          .map((m) => (m.entry.startNumber != null ? `nr. ${m.entry.startNumber}` : m.entry.groupName))
                          .join(', ')}
                        {missing.filter((m) => m.pass === p).length > 6 ? ' …' : ''}
                      </AppText>
                    ))}
                  <AppText variant="caption" color={brand.navy}>
                    Een gemiste passage telt niet mee: het gemiddelde gaat over de passages die je wel hebt ingevuld.
                  </AppText>
                </View>
              ) : null}
              {scoredOutside.length > 0 ? (
                <Card style={styles.card}>
                  <AppText variant="overline" color={colors.textSecondary}>
                    Buiten jouw categorieën
                  </AppText>
                  {scoredOutside.map((e) => (
                    <View key={e.registrationId} style={styles.row}>
                      <AppText variant="body" style={styles.flex}>
                        {e.startNumber != null ? `nr. ${e.startNumber} ` : ''}
                        {e.groupName}
                      </AppText>
                      <AppText variant="caption" color={colors.textSecondary}>
                        wacht op akkoord
                      </AppText>
                    </View>
                  ))}
                </Card>
              ) : null}
              {judging.pending > 0 ? (
                <AppText variant="caption" color={colors.textSecondary}>
                  {judging.syncing ? 'Scores versturen…' : `${judging.pending} ${judging.pending === 1 ? 'score wacht' : 'scores wachten'} op internet.`}
                </AppText>
              ) : null}
              {message ? (
                <AppText variant="bodyStrong" color={colors.accentText} accessibilityRole="alert">
                  {message}
                </AppText>
              ) : null}
              {submitting ? <ActivityIndicator /> : null}
              <Button
                label="Jurering indienen"
                disabled={submitting}
                onPress={() => confirmSubmit(assigned.length + scoredOutside.length, missing.length)}
              />
              <Button label="Terug naar de optocht" variant="secondary" onPress={() => goTo(0)} />
            </ScrollView>
          )
        }
      />

      <View style={[styles.footer, { paddingBottom: insets.bottom + 8 }]}>
        <Pressable onPress={() => goTo(index - 1)} disabled={index === 0} accessibilityRole="button" accessibilityLabel="Vorige" hitSlop={8}>
          <AppText variant="link" color={index === 0 ? colors.textTertiary : colors.linkText}>
            {index > 0 ? `‹ ${neighbour(index - 1)}` : ' '}
          </AppText>
        </Pressable>
        <View style={styles.dots} importantForAccessibility="no-hide-descendants">
          {Array.from({ length: Math.min(7, pages.length) }, (_, i) => {
            const on = i === Math.min(3, index, Math.max(0, 6 - (pages.length - 1 - index)));
            return <View key={i} style={[styles.dot, { backgroundColor: on ? brand.red : colors.border }, on && styles.dotOn]} />;
          })}
        </View>
        <Pressable
          onPress={() => goTo(index + 1)}
          disabled={index >= pages.length - 1}
          accessibilityRole="button"
          accessibilityLabel="Volgende"
          hitSlop={8}
        >
          <AppText variant="link" color={colors.linkText}>
            {index < pages.length - 1 ? `${neighbour(index + 1)} ›` : ' '}
          </AppText>
        </Pressable>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1 },
  center: { alignItems: 'center', justifyContent: 'center', paddingHorizontal: 28, gap: 16 },
  centerText: { textAlign: 'center' },
  okCircle: { width: 96, height: 96, borderRadius: 48, alignItems: 'center', justifyContent: 'center' },
  header: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingRight: 20 },
  headerRight: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  chip: { paddingHorizontal: 10, paddingVertical: 5, borderRadius: radius.pill },
  page: { paddingHorizontal: 20, paddingBottom: 24, gap: 12 },
  banner: { padding: 12, borderRadius: radius.sm, gap: 4 },
  entryHead: { flexDirection: 'row', alignItems: 'center', gap: 14 },
  number: { width: 72, height: 72, borderRadius: radius.md, alignItems: 'center', justifyContent: 'center' },
  numberText: { lineHeight: 34 },
  flex: { flex: 1 },
  category: { alignSelf: 'flex-start', paddingHorizontal: 10, paddingVertical: 3, borderRadius: radius.pill, marginTop: 6 },
  segmented: { flexDirection: 'row', padding: 4, borderRadius: radius.sm, gap: 4 },
  segment: { flex: 1, alignItems: 'center', paddingVertical: 9, borderRadius: 9 },
  segmentActive: { shadowColor: '#000000', shadowOpacity: 0.1, shadowRadius: 3, shadowOffset: { width: 0, height: 1 }, elevation: 1 },
  sliders: { gap: 12, padding: 16, borderRadius: radius.md },
  card: { gap: 10, padding: 16, borderRadius: radius.md },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  footer: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingHorizontal: 20, paddingTop: 8 },
  dots: { flexDirection: 'row', alignItems: 'center', gap: 5 },
  dot: { width: 6, height: 6, borderRadius: 3 },
  dotOn: { width: 8, height: 8, borderRadius: 4 },
});
