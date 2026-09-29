import { StyleSheet, View } from 'react-native';
import { queryKeys, useArrivalTimes } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Card, EmptyState, LargeTitleHeader, QueryState, Screen } from '../../ui';

const date = new Intl.DateTimeFormat('nl-NL', {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  timeZone: 'Europe/Amsterdam',
});

/** Aanrijtijden van de wagens (fase 16), voor iedereen: zoals de tabel op de website, na publiceren. */
export default function AanrijtijdenScreen() {
  const { colors } = useTheme();
  const arrivals = useArrivalTimes();
  const refresh = useRefresh([queryKeys.arrivalTimes]);
  const d = arrivals.data;

  return (
    <Screen {...refresh}>
      <BackLink label="Optocht" />
      <LargeTitleHeader title="Aanrijtijden" />
      <View style={styles.content}>
        <QueryState query={arrivals} />
        {d && !d.published ? (
          <EmptyState
            title="Nog niet bekend"
            message="De aanrijtijden van de wagens verschijnen hier zodra de optochtcommissie ze bekendmaakt."
          />
        ) : null}
        {d?.published ? (
          <>
            <AppText variant="body" color={colors.textSecondary}>
              {[d.paradeName, d.paradeDate ? date.format(new Date(`${d.paradeDate}T12:00:00Z`)) : null]
                .filter(Boolean)
                .join(' · ')}
              {d.location ? `. Tijd waarop de wagen bij ${d.location} moet zijn.` : ''}
            </AppText>
            <Card style={styles.card}>
              {d.rows.map((row, i) => (
                <View
                  key={`${row.startNumber}-${row.groupName}`}
                  style={[
                    styles.row,
                    i > 0 && { borderTopColor: colors.border, borderTopWidth: StyleSheet.hairlineWidth },
                  ]}
                  accessible
                  accessibilityLabel={`Startnummer ${row.startNumber}, ${row.groupName}, ${row.category}: ${row.arrivalTime} uur`}
                >
                  <AppText variant="bodyStrong" style={styles.number}>
                    {row.startNumber}
                  </AppText>
                  <View style={styles.flex}>
                    <AppText variant="bodyStrong">{row.groupName}</AppText>
                    <AppText variant="caption" color={colors.textSecondary}>
                      {row.category}
                    </AppText>
                  </View>
                  <AppText variant="bodyStrong">{row.arrivalTime}</AppText>
                </View>
              ))}
            </Card>
          </>
        ) : null}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16, paddingBottom: 24 },
  card: { paddingVertical: 4, paddingHorizontal: 16 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingVertical: 10 },
  number: { width: 32 },
  flex: { flex: 1 },
});
