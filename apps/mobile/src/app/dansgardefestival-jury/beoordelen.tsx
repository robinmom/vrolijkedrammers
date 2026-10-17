import { brand, radius } from '@drammers/design-tokens';
import { useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useMemo, useRef, useState } from 'react';
import { ActivityIndicator, Alert, FlatList, Pressable, ScrollView, StyleSheet, useWindowDimensions, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { api, ApiError, unwrap } from '../../api/client';
import { queryKeys, useFestivalJurySession, useMe } from '../../api/queries';
import { danceComplete, danceLabel, danceTotal, festivalCriteria, festivalScoreKey, MAX_SCORE, type FestivalDance } from '../../features/festivalJudging';
import { useFestivalJudging } from '../../features/useFestivalJudging';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, QueryState, ScoreSlider, TextField } from '../../ui';

type Page = { kind: 'dance'; dance: FestivalDance } | { kind: 'end' };

/**
 * F2–F6 Beoordelen op het Dansgardefestival (2026-10-15, Figma "💃 Dansgardefestival"): swipen tussen de dansen van de
 * categorie die aan de beurt is. Per dans vier onderdelen van 0 tot 10 (solo: Ritme), het totaal en een opmerking voor
 * de vereniging. Aan het einde de categorie indienen; dat kan pas als alle dansen punten hebben.
 */
export default function FestivalBeoordelenScreen() {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  const { width } = useWindowDimensions();
  const params = useLocalSearchParams<{ dans?: string }>();
  const me = useMe();
  const session = useFestivalJurySession(me.data?.permissions.includes('festival.judge') ?? false);
  const judging = useFestivalJudging(session.data, me.data?.id);
  const client = useQueryClient();
  const [chosen, setChosen] = useState<number | null>(null);
  const [sliding, setSliding] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const list = useRef<FlatList<Page>>(null);
  const s = session.data;
  const category = s?.current ?? null;
  const criteria = festivalCriteria(category?.solo ?? false);

  const pages: Page[] = useMemo(() => [...(s?.dances ?? []).map((dance) => ({ kind: 'dance' as const, dance })), { kind: 'end' as const }], [s]);
  const initialIndex = Math.min(pages.length - 1, Math.max(0, Number(params.dans) || 0));
  const index = chosen ?? initialIndex;

  const goTo = (i: number) => {
    const target = Math.max(0, Math.min(pages.length - 1, i));
    setChosen(target);
    list.current?.scrollToIndex({ index: target, animated: true });
  };

  async function submit() {
    if (!s || !category) return;
    setMessage(null);
    setSubmitting(true);
    try {
      if (!(await judging.sync())) {
        setMessage('Geen verbinding: je punten staan veilig op je telefoon. Indienen kan zodra er weer internet is.');
        return;
      }
      await unwrap(api.POST('/api/v1/festival-jury/submit', { body: { categoryKey: category.key } }));
      await client.invalidateQueries({ queryKey: queryKeys.festivalJury });
      setChosen(0);
      list.current?.scrollToOffset({ offset: 0, animated: false });
    } catch (error) {
      setMessage(
        error instanceof ApiError && error.status === 422
          ? 'Nog niet alle dansen hebben punten voor alle onderdelen.'
          : 'Indienen lukt nu niet. Je punten staan veilig op je telefoon; probeer het zo opnieuw.',
      );
    } finally {
      setSubmitting(false);
    }
  }

  if (!s) {
    return (
      <View style={[styles.screen, { paddingTop: insets.top, backgroundColor: colors.canvas }]}>
        <BackLink label="Jureren" />
        <QueryState query={session} />
      </View>
    );
  }

  if (!category) {
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
          Alle categorieën zijn ingediend. De organisatie maakt de uitslag; de hoofdjury reikt de prijzen uit.
        </AppText>
        <Button label="Terug" variant="secondary" onPress={() => router.replace('/dansgardefestival-jury')} />
      </View>
    );
  }

  const missing = s.dances.filter((d) => !danceComplete(judging.scores, d.entryId));
  const current = pages[index];
  const neighbour = (i: number) => {
    const p = pages[i];
    return p?.kind === 'dance' ? p.dance.names : p ? 'Einde' : null;
  };
  const confirmSubmit = () =>
    Alert.alert(`${category.name} indienen?`, `Je dient de punten en opmerkingen in van ${s.dances.length} ${s.dances.length === 1 ? 'dans' : 'dansen'}. Daarna kun je ze niet meer wijzigen.`, [
      { text: 'Annuleren', style: 'cancel' },
      { text: 'Ja, indienen', style: 'destructive', onPress: () => void submit() },
    ]);

  return (
    <View style={[styles.screen, { paddingTop: insets.top, backgroundColor: colors.canvas }]}>
      <View style={styles.header}>
        <BackLink label="Jureren" />
        <View style={styles.headerRight}>
          <AppText variant="caption" color={colors.textSecondary}>
            {current?.kind === 'dance' ? `Dans ${index + 1} van ${s.dances.length}` : 'Einde categorie'}
          </AppText>
          <View style={[styles.chip, { backgroundColor: colors.tintBlue }]}>
            <AppText variant="label" color={colors.linkText}>
              {category.name}
            </AppText>
          </View>
        </View>
      </View>

      <FlatList
        ref={list}
        data={pages}
        horizontal
        pagingEnabled
        scrollEnabled={!sliding}
        showsHorizontalScrollIndicator={false}
        keyExtractor={(p) => (p.kind === 'dance' ? p.dance.entryId : 'einde')}
        getItemLayout={(_, i) => ({ length: width, offset: width * i, index: i })}
        initialScrollIndex={initialIndex}
        onMomentumScrollEnd={(e) => setChosen(Math.round(e.nativeEvent.contentOffset.x / width))}
        renderItem={({ item }) =>
          item.kind === 'dance' ? (
            <ScrollView style={{ width }} contentContainerStyle={styles.page} keyboardShouldPersistTaps="handled">
              <View style={styles.entryHead}>
                <View style={[styles.number, { backgroundColor: brand.navy }]} accessible accessibilityLabel={`Dans ${item.dance.number}`}>
                  <AppText variant="label" color="rgba(255,255,255,0.75)">
                    NR
                  </AppText>
                  <AppText variant="largeTitle" color="#FFFFFF" style={styles.numberText}>
                    {item.dance.number}
                  </AppText>
                </View>
                <View style={styles.flex}>
                  <AppText variant="cardTitle" accessibilityRole="header">
                    {danceLabel(item.dance)}
                  </AppText>
                  <View style={[styles.category, { backgroundColor: colors.tintBlue }]}>
                    <AppText variant="label" color={colors.linkText}>
                      {item.dance.club}
                    </AppText>
                  </View>
                </View>
              </View>
              <AppText variant="body" color={colors.textSecondary}>
                {item.dance.dancerCount} {item.dance.dancerCount === 1 ? 'danser' : 'dansers'} · gem. {String(item.dance.averageAge).replace('.', ',')} jr
              </AppText>
              <Card style={styles.sliders}>
                {criteria.map((c) => (
                  <ScoreSlider
                    key={c.key}
                    label={c.label}
                    max={MAX_SCORE}
                    value={judging.scores.get(festivalScoreKey(item.dance.entryId, c.key))?.value ?? null}
                    onChange={(v) => judging.setScore(item.dance.entryId, c.key, v)}
                    onSlidingStart={() => setSliding(true)}
                    onSlidingEnd={() => setSliding(false)}
                  />
                ))}
                <View style={[styles.total, { borderTopColor: colors.border }]}>
                  <AppText variant="bodyStrong" style={styles.flex}>
                    Totaal deze dans
                  </AppText>
                  <AppText variant="cardTitle">
                    {danceTotal(judging.scores, item.dance.entryId)} / {criteria.length * MAX_SCORE}
                  </AppText>
                </View>
                <TextField
                  label="Opmerking voor de vereniging"
                  hint="Komt anoniem in het rapport (met je jurynummer). Wordt direct op je telefoon bewaard."
                  multiline
                  maxLength={1000}
                  value={judging.comments.get(item.dance.entryId) ?? ''}
                  onChangeText={(text) => judging.setComment(item.dance.entryId, text)}
                />
              </Card>
            </ScrollView>
          ) : (
            <ScrollView style={{ width }} contentContainerStyle={styles.page}>
              <AppText variant="heroTitle" accessibilityRole="header">
                Klaar met {category.name}?
              </AppText>
              <AppText variant="body" color={colors.textSecondary}>
                Controleer je punten en dien de categorie in. Daarna kun je niets meer wijzigen en komt de volgende categorie.
              </AppText>
              <Card style={styles.card}>
                <AppText variant="overline" color={colors.textSecondary}>
                  Overzicht
                </AppText>
                {s.dances.map((d) => {
                  const complete = danceComplete(judging.scores, d.entryId);
                  return (
                    <Pressable key={d.entryId} style={styles.row} onPress={() => goTo(s.dances.indexOf(d))} accessibilityRole="button">
                      <AppText variant="bodyStrong" style={styles.flex}>
                        {d.number} · {d.names}
                      </AppText>
                      <AppText variant="link" color={complete ? colors.successText : colors.accentText}>
                        {complete ? `${danceTotal(judging.scores, d.entryId)} ✓` : 'open'}
                      </AppText>
                    </Pressable>
                  );
                })}
              </Card>
              {missing.length > 0 ? (
                <View style={[styles.banner, { backgroundColor: `${brand.yellow}40` }]}>
                  <AppText variant="bodyStrong" color={brand.navy}>
                    Nog niet beoordeeld
                  </AppText>
                  <AppText variant="caption" color={brand.navy}>
                    {missing.map((d) => `${d.number} · ${d.names}`).join('  ·  ')}
                  </AppText>
                  <AppText variant="caption" color={brand.navy}>
                    Indienen kan pas als alle dansen van deze categorie punten hebben. Swipe terug of tik op de dans.
                  </AppText>
                </View>
              ) : null}
              {judging.pending > 0 ? (
                <AppText variant="caption" color={colors.textSecondary}>
                  {judging.syncing ? 'Versturen…' : `${judging.pending} ${judging.pending === 1 ? 'wijziging wacht' : 'wijzigingen wachten'} op internet.`}
                </AppText>
              ) : null}
              {message ? (
                <AppText variant="bodyStrong" color={colors.accentText} accessibilityRole="alert">
                  {message}
                </AppText>
              ) : null}
              {submitting ? <ActivityIndicator /> : null}
              <Button label="Categorie indienen" disabled={submitting || missing.length > 0} onPress={confirmSubmit} />
              <Button label="Terug naar de dansen" variant="secondary" onPress={() => goTo(0)} />
            </ScrollView>
          )
        }
      />

      <View style={[styles.footer, { paddingBottom: insets.bottom + 8 }]}>
        <Pressable onPress={() => goTo(index - 1)} disabled={index === 0} accessibilityRole="button" accessibilityLabel="Vorige dans" hitSlop={8}>
          <AppText variant="link" color={index === 0 ? colors.textTertiary : colors.linkText}>
            {index > 0 ? `‹ ${neighbour(index - 1)}` : ' '}
          </AppText>
        </Pressable>
        <View style={styles.dots} importantForAccessibility="no-hide-descendants">
          {pages.map((p, i) => (
            <View key={p.kind === 'dance' ? p.dance.entryId : 'einde'} style={[styles.dot, { backgroundColor: i === index ? brand.red : colors.border }, i === index && styles.dotOn]} />
          ))}
        </View>
        <Pressable onPress={() => goTo(index + 1)} disabled={index >= pages.length - 1} accessibilityRole="button" accessibilityLabel="Volgende dans" hitSlop={8}>
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
  chip: { paddingHorizontal: 10, paddingVertical: 5, borderRadius: radius.pill, flexShrink: 1 },
  page: { paddingHorizontal: 20, paddingBottom: 24, gap: 12 },
  banner: { padding: 12, borderRadius: radius.sm, gap: 4 },
  entryHead: { flexDirection: 'row', alignItems: 'center', gap: 14 },
  number: { width: 72, height: 72, borderRadius: radius.md, alignItems: 'center', justifyContent: 'center' },
  numberText: { lineHeight: 34 },
  flex: { flex: 1 },
  category: { alignSelf: 'flex-start', paddingHorizontal: 10, paddingVertical: 3, borderRadius: radius.pill, marginTop: 6 },
  sliders: { gap: 12, padding: 16, borderRadius: radius.md },
  total: { flexDirection: 'row', alignItems: 'center', borderTopWidth: StyleSheet.hairlineWidth, paddingTop: 12 },
  card: { gap: 10, padding: 16, borderRadius: radius.md },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: 32 },
  footer: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingHorizontal: 20, paddingTop: 8 },
  dots: { flexDirection: 'row', alignItems: 'center', gap: 5 },
  dot: { width: 6, height: 6, borderRadius: 3 },
  dotOn: { width: 8, height: 8, borderRadius: 4 },
});
