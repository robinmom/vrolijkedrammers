import { brand, radius } from '@drammers/design-tokens';
import { useMemo } from 'react';
import { StyleSheet, View } from 'react-native';
import { queryKeys, useNews, useParadeResults } from '../api/queries';
import { useRefresh } from '../api/useRefresh';
import { NewsFeed } from '../features/NewsFeed';
import { useTheme } from '../theme/ThemeProvider';
import { AppText, BackLink, Card, EmptyState, LargeTitleHeader, QueryState, Screen, SectionHeader } from '../ui';

const points = (n: number) => n.toLocaleString('nl-NL', { maximumFractionDigits: 1 });

/**
 * Uitslagen: de gepubliceerde uitslag van de optocht (fase 22c, pas na de prijsuitreiking) en nieuwsberichten met
 * categorie "Uitslagen" (OQ-40).
 */
export default function UitslagenScreen() {
  const { colors } = useTheme();
  const news = useNews();
  const results = useParadeResults();
  const refresh = useRefresh([queryKeys.news, queryKeys.paradeResults]);
  const items = useMemo(() => (news.data ?? []).filter((n) => n.category?.trim().toLowerCase() === 'uitslagen'), [news.data]);
  const parade = results.data;
  return (
    <Screen {...refresh}>
      <BackLink label="Terug" />
      <LargeTitleHeader title="Uitslagen" />
      {parade ? (
        <View style={styles.results}>
          <SectionHeader title={`Uitslag ${parade.paradeName}`} />
          {parade.categories.map((c) => (
            <Card key={c.name} style={styles.card}>
              <AppText variant="listTitle" accessibilityRole="header">
                {c.name}
              </AppText>
              {c.rows.map((r) => (
                <View
                  key={`${r.place}-${r.groupName}`}
                  style={[styles.row, { borderTopColor: colors.border }]}
                  accessible
                  accessibilityLabel={`${r.place}e plaats: ${r.groupName}${r.startNumber != null ? `, startnummer ${r.startNumber}` : ''}, ${points(r.total)} punten`}
                >
                  <View style={[styles.place, { backgroundColor: r.place === 1 ? brand.yellow : colors.surfaceMuted }]}>
                    <AppText variant="bodyStrong">{r.place}</AppText>
                  </View>
                  <View style={styles.flex}>
                    <AppText variant="bodyStrong">{r.groupName}</AppText>
                    {r.motto ? (
                      <AppText variant="caption" color={colors.textSecondary}>
                        {r.motto}
                      </AppText>
                    ) : null}
                  </View>
                  <AppText variant="link">{points(r.total)}</AppText>
                </View>
              ))}
            </Card>
          ))}
        </View>
      ) : null}
      {!news.data ? (
        <QueryState query={news} />
      ) : items.length === 0 && !parade ? (
        <EmptyState icon="uitslagen" title="Nog geen uitslagen" message="Na de optocht en andere wedstrijden verschijnen de uitslagen hier." />
      ) : (
        <NewsFeed items={items} />
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  results: { paddingHorizontal: 20, gap: 12, paddingBottom: 16 },
  card: { padding: 16, gap: 4, borderRadius: radius.md },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingVertical: 10, borderTopWidth: StyleSheet.hairlineWidth },
  place: { width: 32, height: 32, borderRadius: 16, alignItems: 'center', justifyContent: 'center' },
  flex: { flex: 1 },
});
