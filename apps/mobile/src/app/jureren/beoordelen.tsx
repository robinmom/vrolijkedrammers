import { brand, radius } from '@drammers/design-tokens';
import { useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useMemo, useRef, useState } from 'react';
import { ActivityIndicator, Alert, FlatList, Pressable, ScrollView, StyleSheet, useWindowDimensions, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { api, unwrap } from '../../api/client';
import { queryKeys, useJurySession, useMe } from '../../api/queries';
import {
  carriedOver,
  criteria,
  entryLabel,
  entryTotal,
  firstIncompletePass,
  maxTotal,
  passComplete,
  passes,
  valueOf,
  type JudgingEntry,
} from '../../features/judging';
import { useJudging } from '../../features/useJudging';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, QueryState, ScoreSlider } from '../../ui';

type Page = { kind: 'entry'; entry: JudgingEntry; pass: number } | { kind: 'end' };

/**
 * J2–J7 Beoordelen (fase 22b, Figma "⚖️ Jury"): van links naar rechts door de optocht swipen. Per wagen of groep het
 * startnummer, de groep en het motto, dan per voorbijtrekken (1, 2, 3) vier sliders. Standaard alleen de eigen
 * categorieën; met "Hele optocht" ook de rest (dat telt pas mee na akkoord). Aan het einde: jurering indienen.
 *
 * Het swipen loopt door álle voorbijtrekkens: na de laatste wagen van voorbij 1 komt de eerste wagen van voorbij 2, en
 * na voorbij 3 het einde. Het actieve voorbijtrekken volgt dus uit de pagina waar je staat, niet uit losse state.
 */
export default function BeoordelenScreen() {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  const { width } = useWindowDimensions();
  const params = useLocalSearchParams<{ start?: string; passage?: string; einde?: string }>();
  const me = useMe();
  const session = useJurySession(me.data?.permissions.includes('parade.judge') ?? false);
  const judging = useJudging(session.data, me.data?.id);
  const client = useQueryClient();
  const [whole, setWhole] = useState(false);
  const [chosen, setChosen] = useState<number | null>(null);
  // Waar het jurylid was toen hij naar het overzicht ging, zodat hij daar weer terugkomt.
  const [returnTo, setReturnTo] = useState<number | null>(null);
  const [submitting, setSubmitting] = useState(false);
  // Tijdens het slepen van een slider niet naar de volgende wagen swipen.
  const [sliding, setSliding] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const list = useRef<FlatList<Page>>(null);
  const s = session.data;
  const submitted = Boolean(s?.submittedAt);

  const entries = useMemo(() => (s?.entries ?? []).filter((e) => whole || e.assigned), [s, whole]);
  // Eerst alle wagens van voorbij 1, dan die van voorbij 2, enzovoort, en als laatste de afrondpagina.
  const pages: Page[] = useMemo(
    () => [...passes.flatMap((pass) => entries.map((entry) => ({ kind: 'entry' as const, entry, pass }))), { kind: 'end' as const }],
    [entries],
  );

  // Beginnen bij de inzending (of het einde) waar het startscherm naar verwees.
  const initialIndex = useMemo(() => {
    if (params.einde) return pages.length - 1;
    const wanted = Number(params.passage) || 1;
    return Math.max(0, pages.findIndex((p) => p.kind === 'entry' && p.entry.registrationId === params.start && p.pass === wanted));
  }, [pages, params.einde, params.passage, params.start]);
  const index = chosen ?? initialIndex;
  const setIndex = setChosen;
  // Pass-major: de index van een wagen in een voorbijtrekken is te berekenen, zodat de tabs ernaartoe kunnen springen.
  const indexFor = (pass: number, entryIndex: number) => (pass - 1) * entries.length + entryIndex;

  const goTo = (i: number) => {
    const target = Math.max(0, Math.min(pages.length - 1, i));
    setIndex(target);
    list.current?.scrollToIndex({ index: target, animated: true });
  };

  // Het aantal wagens verandert, en daarmee wat elke index betekent: daarom altijd terug naar het begin.
  const restart = (next: boolean) => {
    setWhole(next);
    setIndex(0);
    list.current?.scrollToOffset({ offset: 0, animated: false });
  };

  const toggleWhole = () => {
    if (whole) {
      restart(false);
      return;
    }
    Alert.alert(
      'Je gaat nu de hele optocht beoordelen',
      'Je ziet dan ook de wagens en groepen uit categorieën die niet aan jou zijn toegewezen. Scores daarvan tellen pas mee als het bestuur of de hoofdjury ze goedkeurt; die krijgen hier een melding van.',
      [
        { text: 'Alleen mijn categorieën', style: 'cancel' },
        { text: 'Ja, hele optocht beoordelen', onPress: () => restart(true) },
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
  // Positie binnen het huidige voorbijtrekken (de pagina's staan pass-major achter elkaar).
  const posInPass = current?.kind === 'entry' ? index % Math.max(1, entries.length) : -1;
  const neighbour = (i: number) => {
    const p = pages[i];
    if (!p) return null;
    if (p.kind === 'end') return 'Einde';
    const name = p.entry.startNumber != null ? `nr. ${p.entry.startNumber}` : p.entry.groupName;
    // Bij de overgang naar een volgend voorbijtrekken erbij zetten dat het voorbijtrekken wisselt.
    return current?.kind === 'entry' && p.pass !== current.pass ? `Voorbij ${p.pass} · ${name}` : name;
  };

  /** Per criterium de eigen waarde in dit voorbijtrekken en wat er uit een eerder voorbijtrekken over te nemen is. */
  const criteriaFor = (entry: JudgingEntry, pass: number) =>
    criteria.map((c) => ({
      criterion: c,
      own: valueOf(judging.scores, entry.registrationId, pass, c.key),
      carried: carriedOver(judging.scores, entry.registrationId, pass, c.key),
    }));
  /** Wat nog bevestigd kan worden: een overgenomen waarde zonder eigen waarde in dit voorbijtrekken. */
  const toConfirm = (entry: JudgingEntry, pass: number) =>
    criteriaFor(entry, pass).flatMap((x) => (x.own === null && x.carried ? [{ key: x.criterion.key, value: x.carried.value }] : []));

  /** Naar een wagen of groep, standaard bij het eerste voorbijtrekken dat nog niet af is (vanuit het overzicht). */
  const goToEntry = (entry: JudgingEntry, pass?: number) => {
    const i = entries.findIndex((e) => e.registrationId === entry.registrationId);
    if (i < 0) return;
    goTo(indexFor(pass ?? firstIncompletePass(judging.scores, entry.registrationId), i));
  };

  return (
    <View style={[styles.screen, { paddingTop: insets.top, backgroundColor: colors.canvas }]}>
      <View style={styles.header}>
        <BackLink label="Jureren" />
        <View style={styles.headerRight}>
          <AppText variant="caption" color={colors.textSecondary} numberOfLines={1}>
            {current?.kind === 'entry' ? `${posInPass + 1} van ${entries.length}` : 'Einde optocht'}
          </AppText>
          {/* Altijd bij het overzicht kunnen: daar staan de totalen, de gaten en het indienen. */}
          <Pressable
            onPress={() => {
              setReturnTo(current?.kind === 'entry' ? index : null);
              goTo(pages.length - 1);
            }}
            accessibilityRole="button"
            accessibilityLabel="Overzicht"
            style={[styles.chip, styles.chipFixed, { backgroundColor: colors.tintBlue }]}
            hitSlop={6}
          >
            <AppText variant="label" color={colors.linkText}>
              Overzicht
            </AppText>
          </Pressable>
          <Pressable
            onPress={toggleWhole}
            accessibilityRole="switch"
            accessibilityState={{ checked: whole }}
            accessibilityLabel="Hele optocht"
            style={[styles.chip, styles.chipFixed, { backgroundColor: whole ? `${brand.yellow}59` : colors.tintBlue }]}
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
        scrollEnabled={!sliding}
        showsHorizontalScrollIndicator={false}
        keyExtractor={(p) => (p.kind === 'entry' ? `${p.pass}|${p.entry.registrationId}` : 'einde')}
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
                  const active = p === item.pass;
                  return (
                    <Pressable
                      key={p}
                      onPress={() => goTo(indexFor(p, entries.findIndex((e) => e.registrationId === item.entry.registrationId)))}
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
              <Card style={styles.sliders}>
                {criteriaFor(item.entry, item.pass).map(({ criterion, own, carried }) => (
                  <ScoreSlider
                    key={criterion.key}
                    label={criterion.label}
                    value={own}
                    suggested={carried?.value ?? null}
                    suggestedHint={carried ? `${carried.value}, overgenomen uit voorbij ${carried.fromPass}, nog niet bevestigd` : undefined}
                    onChange={(v) => judging.setScore(item.entry.registrationId, item.pass, criterion.key, v)}
                    onSlidingStart={() => setSliding(true)}
                    onSlidingEnd={() => setSliding(false)}
                  />
                ))}
              </Card>
              {toConfirm(item.entry, item.pass).length > 0 ? (
                <View style={styles.carried}>
                  <AppText variant="caption" color={colors.textSecondary}>
                    De grijze waardes komen uit een eerder voorbijtrekken en tellen nog niet mee. Raak ze aan, of neem ze in één keer over.
                  </AppText>
                  <Button
                    label="Vorige waardes overnemen"
                    variant="secondary"
                    onPress={() => {
                      for (const { key, value } of toConfirm(item.entry, item.pass)) {
                        judging.setScore(item.entry.registrationId, item.pass, key, value);
                      }
                    }}
                  />
                </View>
              ) : null}
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
                  Voortgang
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
              <Card style={styles.card}>
                <AppText variant="overline" color={colors.textSecondary}>
                  Jouw punten
                </AppText>
                <AppText variant="caption" color={colors.textSecondary}>
                  Alleen jouw eigen punten, zoals ze meetellen: per criterium het gemiddelde over de voorbijtrekkens die je hebt
                  ingevuld, maal de weging van de categorie. Tik op een wagen of groep om hem na te kijken.
                </AppText>
                {assigned.map((e) => {
                  const total = entryTotal(judging.scores, e, s.weights);
                  const max = maxTotal(e, s.weights);
                  const open = passes.filter((p) => !passComplete(judging.scores, e.registrationId, p)).length;
                  return (
                    <Pressable
                      key={e.registrationId}
                      onPress={() => goToEntry(e)}
                      accessibilityRole="button"
                      accessibilityLabel={`${entryLabel(e)}, ${
                        total === null ? 'nog geen punten' : `${total} punten${max ? ` van ${max}` : ''}`
                      }${open > 0 ? `, ${open} ${open === 1 ? 'voorbijtrekken' : 'voorbijtrekkens'} nog niet ingevuld` : ''}`}
                      style={styles.row}
                    >
                      <AppText variant="bodyStrong" style={styles.flex} numberOfLines={1}>
                        {entryLabel(e)}
                      </AppText>
                      <AppText variant="link" color={open === 0 ? colors.successText : colors.accentText}>
                        {total === null ? 'open' : `${total}${open > 0 ? ' •' : ' ✓'}`}
                      </AppText>
                    </Pressable>
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
                  {/* Direct naar het eerste gat, zodat je niet zelf hoeft te zoeken waar je gebleven was. */}
                  <Button
                    label="Ga naar inzending"
                    variant="secondary"
                    onPress={() => {
                      const first = missing[0];
                      if (first) goToEntry(first.entry, first.pass);
                    }}
                  />
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
              <Button
                label="Terug naar het jureren"
                variant="secondary"
                onPress={() => {
                  goTo(returnTo ?? 0);
                  setReturnTo(null);
                }}
              />
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
          {/* Stippen tellen binnen het huidige voorbijtrekken, niet over alle voorbijtrekkens heen. */}
          {Array.from({ length: Math.min(7, Math.max(1, entries.length)) }, (_, i) => {
            const on = posInPass >= 0 && i === Math.min(3, posInPass, Math.max(0, 6 - (entries.length - 1 - posInPass)));
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
  headerRight: { flexDirection: 'row', alignItems: 'center', gap: 8, flexShrink: 1 },
  chip: { paddingHorizontal: 10, paddingVertical: 5, borderRadius: radius.pill },
  // De knoppen houden hun breedte; op een smal toestel krimpt de teller.
  chipFixed: { flexShrink: 0 },
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
  carried: { gap: 8 },
  card: { gap: 10, padding: 16, borderRadius: radius.md },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  footer: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingHorizontal: 20, paddingTop: 8 },
  dots: { flexDirection: 'row', alignItems: 'center', gap: 5 },
  dot: { width: 6, height: 6, borderRadius: 3 },
  dotOn: { width: 8, height: 8, borderRadius: 4 },
});
