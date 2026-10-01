import { radius } from '@drammers/design-tokens';
import { router } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { queryKeys, useJurySession, useMe } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { passComplete, passes } from '../../features/judging';
import { useJudging } from '../../features/useJudging';
import { fullDate } from '../../lib/dates';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Badge, Button, Card, EmptyState, LargeTitleHeader, QueryState, Screen } from '../../ui';

/**
 * J1 Jureren (fase 22b, Figma "⚖️ Jury"): de optocht, de eigen categorieën, de voortgang per voorbijtrekken en verder
 * jureren waar je gebleven was. Scores staan op de telefoon, dus dit werkt ook zonder netwerk.
 */
export default function JurerenScreen() {
  const { colors } = useTheme();
  const me = useMe();
  const canJudge = me.data?.permissions.includes('parade.judge') ?? false;
  const session = useJurySession(canJudge);
  const judging = useJudging(session.data);
  const refresh = useRefresh([queryKeys.jury]);
  const s = session.data;
  const mine = (s?.entries ?? []).filter((e) => e.assigned);

  // Verder bij de eerste toegewezen inzending die in de laagste onvolledige passage nog open staat.
  const nextPass = passes.find((p) => mine.some((e) => !passComplete(judging.scores, e.registrationId, p)));
  const next = nextPass ? mine.find((e) => !passComplete(judging.scores, e.registrationId, nextPass)) : undefined;

  return (
    <Screen {...refresh}>
      <BackLink label="Optocht" />
      <View style={styles.titleRow}>
        <LargeTitleHeader title="Jureren" />
        <Badge label="Jurylid" variant="highlight" />
      </View>
      {!canJudge && me.isSuccess ? (
        <EmptyState icon="uitslagen" title="Geen jurylid" message="Alleen juryleden kunnen jureren. Vraag het bestuur als dit niet klopt." />
      ) : !s ? (
        <QueryState query={session} />
      ) : (
        <View style={styles.content}>
          <AppText variant="body" color={colors.textSecondary}>
            {s.paradeName} · {fullDate(`${s.paradeDate}T12:00:00Z`).toLowerCase()}, start {s.startTime.slice(0, 5)}
          </AppText>
          {s.submittedAt ? (
            <Card style={styles.card}>
              <AppText variant="cardTitle">Je jurering is ingediend</AppText>
              <AppText variant="body" color={colors.textSecondary}>
                Bedankt! De uitslagcommissie maakt de uitslag bekend bij de prijsuitreiking.
              </AppText>
            </Card>
          ) : null}
          <Card style={styles.card}>
            <AppText variant="overline" color={colors.textSecondary}>
              Jouw categorieën
            </AppText>
            {s.categories.length === 0 ? (
              <AppText variant="body" color={colors.textSecondary}>
                Je bent nog niet ingedeeld. Het bestuur of de hoofdjury wijst je categorieën toe.
              </AppText>
            ) : (
              s.categories.map((c) => (
                <View key={c.id} style={styles.row}>
                  <AppText variant="bodyStrong" style={styles.flex}>
                    {c.name}
                  </AppText>
                  <AppText variant="caption" color={colors.textSecondary}>
                    {mine.filter((e) => e.categoryId === c.id).length}
                  </AppText>
                </View>
              ))
            )}
          </Card>
          {mine.length > 0 ? (
            <Card style={styles.card}>
              <AppText variant="overline" color={colors.textSecondary}>
                Voortgang
              </AppText>
              {passes.map((p) => {
                const done = mine.filter((e) => passComplete(judging.scores, e.registrationId, p)).length;
                const complete = done === mine.length;
                return (
                  <View key={p} style={styles.progress} accessible accessibilityLabel={`Voorbijtrekken ${p}: ${done} van ${mine.length}`}>
                    <View style={styles.row}>
                      <AppText variant="bodyStrong" style={styles.flex}>
                        Voorbijtrekken {p}
                      </AppText>
                      <AppText variant="caption" color={complete ? colors.successText : colors.textSecondary}>
                        {done} van {mine.length}
                      </AppText>
                    </View>
                    <View style={[styles.bar, { backgroundColor: colors.surfaceMuted }]}>
                      <View style={[styles.barFill, { width: `${(done / mine.length) * 100}%`, backgroundColor: complete ? colors.successText : colors.accentText }]} />
                    </View>
                  </View>
                );
              })}
            </Card>
          ) : null}
          <View style={[styles.info, { backgroundColor: colors.tintBlue }]}>
            <AppText variant="caption">
              {judging.pending > 0
                ? `${judging.pending} ${judging.pending === 1 ? 'score wacht' : 'scores wachten'} op internet; ze staan veilig op je telefoon.`
                : 'Je scores worden direct op je telefoon bewaard, ook zonder internet. Indienen doe je aan het einde van de optocht.'}
            </AppText>
          </View>
          {!s.submittedAt && s.entries.length > 0 ? (
            <Button
              label={next ? `${judging.scores.size > 0 ? 'Verder jureren' : 'Start jureren'}${next.startNumber != null ? ` (nr. ${next.startNumber})` : ''}` : 'Naar indienen'}
              icon="uitslagen"
              onPress={() =>
                router.push({ pathname: '/jureren/beoordelen', params: next ? { start: next.registrationId, passage: String(nextPass) } : { einde: '1' } })
              }
            />
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
