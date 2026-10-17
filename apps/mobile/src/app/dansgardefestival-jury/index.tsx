import { radius } from '@drammers/design-tokens';
import { router } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { queryKeys, useFestivalJurySession, useMe } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { danceComplete } from '../../features/festivalJudging';
import { useFestivalJudging } from '../../features/useFestivalJudging';
import { fullDate } from '../../lib/dates';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Badge, Button, Card, EmptyState, LargeTitleHeader, QueryState, Screen } from '../../ui';

/**
 * F1 Jureren dansgardefestival (2026-10-15, Figma "💃 Dansgardefestival"): welke categorie nu aan de beurt is, wat daarna
 * komt en de voortgang in het programma. Alleen de dansen van de categorie die op het podium is.
 */
export default function FestivalJuryScreen() {
  const { colors } = useTheme();
  const me = useMe();
  const permissions = me.data?.permissions ?? [];
  const canJudge = permissions.includes('festival.judge');
  const session = useFestivalJurySession(canJudge);
  const judging = useFestivalJudging(session.data, me.data?.id);
  const refresh = useRefresh([queryKeys.festivalJury]);
  const s = session.data;
  const current = s?.current ?? null;
  const index = s && current ? s.categories.findIndex((c) => c.key === current.key) : -1;
  const next = s && index >= 0 ? s.categories[index + 1] : undefined;
  const done = s ? s.dances.filter((d) => danceComplete(judging.scores, d.entryId)).length : 0;
  const firstOpen = s?.dances.findIndex((d) => !danceComplete(judging.scores, d.entryId)) ?? -1;

  return (
    <Screen {...refresh}>
      <BackLink label="Meer" />
      <View style={styles.titleRow}>
        <LargeTitleHeader title="Jureren" />
        {s ? <Badge label={`Jurylid ${s.jurorNumber}`} variant="highlight" /> : null}
      </View>
      {!canJudge && me.isSuccess ? (
        <EmptyState icon="uitslagen" title="Geen jurylid" message="Alleen juryleden van het Dansgardefestival kunnen hier jureren." />
      ) : !s ? (
        <QueryState query={session} />
      ) : (
        <View style={styles.content}>
          <AppText variant="body" color={colors.textSecondary}>
            {s.festivalName} · {fullDate(`${s.festivalDate}T12:00:00Z`).toLowerCase()}, {s.location}
          </AppText>
          {current ? (
            <Card style={styles.card}>
              <AppText variant="overline" color={colors.textSecondary}>
                Nu aan de beurt
              </AppText>
              <View style={styles.row}>
                <AppText variant="bodyStrong" style={styles.flex}>
                  {current.name}
                </AppText>
                <AppText variant="caption" color={colors.textSecondary}>
                  {current.dances} {current.dances === 1 ? 'dans' : 'dansen'}
                </AppText>
              </View>
              {next ? (
                <View style={styles.row}>
                  <AppText variant="body" style={styles.flex}>
                    Daarna: {next.name}
                  </AppText>
                  <AppText variant="caption" color={colors.textSecondary}>
                    {next.dances} {next.dances === 1 ? 'dans' : 'dansen'}
                  </AppText>
                </View>
              ) : null}
            </Card>
          ) : (
            <Card style={styles.card}>
              <AppText variant="cardTitle">Bedankt, je jurering is ingediend</AppText>
              <AppText variant="body" color={colors.textSecondary}>
                Alle categorieën zijn ingediend. De organisatie maakt de uitslag; de hoofdjury reikt de prijzen uit.
              </AppText>
            </Card>
          )}
          <Card style={styles.card}>
            <AppText variant="overline" color={colors.textSecondary}>
              Programma
            </AppText>
            {s.categories.slice(Math.max(0, index - 1), Math.max(0, index - 1) + 5).map((c) => {
              const now = c.key === current?.key;
              const progress = now ? done / Math.max(1, c.dances) : c.submitted ? 1 : 0;
              return (
                <View key={c.key} style={styles.progress} accessible accessibilityLabel={`${c.name}: ${c.submitted ? 'ingediend' : now ? `${done} van ${c.dances}` : 'volgt'}`}>
                  <View style={styles.row}>
                    <AppText variant="bodyStrong" style={styles.flex}>
                      {c.name}
                    </AppText>
                    <AppText variant="caption" color={c.submitted ? colors.successText : colors.textSecondary}>
                      {c.submitted ? 'ingediend ✓' : now ? `${done} van ${c.dances}` : 'volgt'}
                    </AppText>
                  </View>
                  <View style={[styles.bar, { backgroundColor: colors.surfaceMuted }]}>
                    <View style={[styles.barFill, { width: `${progress * 100}%`, backgroundColor: c.submitted ? colors.successText : colors.accentText }]} />
                  </View>
                </View>
              );
            })}
          </Card>
          <View style={[styles.info, { backgroundColor: colors.tintBlue }]}>
            <AppText variant="caption">
              {judging.pending > 0
                ? `${judging.pending} ${judging.pending === 1 ? 'wijziging wacht' : 'wijzigingen wachten'} op internet; ze staan veilig op je telefoon.`
                : 'Je ziet alleen de dansen van de categorie die nu op het podium is. Na de laatste dans dien je de categorie in; daarna komt de volgende.'}
            </AppText>
          </View>
          {current ? (
            <Button
              label={firstOpen >= 0 ? `${done > 0 ? 'Verder jureren' : 'Start jureren'} (dans ${firstOpen + 1})` : 'Naar indienen'}
              icon="uitslagen"
              onPress={() => router.push({ pathname: '/dansgardefestival-jury/beoordelen', params: { dans: String(Math.max(0, firstOpen)) } })}
            />
          ) : null}
          {s.headJury ? (
            <Button label="Hoofdjury: uitslag" variant="secondary" icon="uitslagen" onPress={() => router.push('/dansgardefestival-jury/hoofdjury')} />
          ) : null}
        </View>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  titleRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingRight: 20 },
  content: { paddingHorizontal: 20, gap: 14, paddingBottom: 32 },
  card: { gap: 10, padding: 16, borderRadius: radius.md },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  flex: { flex: 1 },
  progress: { gap: 6 },
  bar: { height: 6, borderRadius: 3, overflow: 'hidden' },
  barFill: { height: 6, borderRadius: 3 },
  info: { padding: 14, borderRadius: radius.sm },
});
