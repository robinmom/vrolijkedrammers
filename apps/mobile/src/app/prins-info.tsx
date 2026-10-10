import { queryKeys, useMyRoyal } from '../api/queries';
import { useRefresh } from '../api/useRefresh';
import { useTheme } from '../theme/ThemeProvider';
import { AppText, BackLink, Card, LargeTitleHeader, QueryState, RichText, Screen } from '../ui';
import { StyleSheet, View } from 'react-native';

/** Informatie voor de prins(es) of de adjudanten (2026-10-10); de tekst beheert het bestuur in het portal onder "Prins". */
export default function PrinsInfoScreen() {
  const { colors } = useTheme();
  const royal = useMyRoyal();
  const refresh = useRefresh([queryKeys.myRoyal]);
  const r = royal.data;
  return (
    <Screen {...refresh}>
      <BackLink label="Home" />
      <LargeTitleHeader title="Info" />
      <View style={styles.content}>
        {r === undefined ? (
          <QueryState query={royal} />
        ) : r === null ? (
          <AppText variant="body" color={colors.textSecondary}>
            Deze informatie is alleen voor de prins(es) en de adjudanten.
          </AppText>
        ) : (
          <>
            <AppText variant="bodyStrong">{r.greeting}!</AppText>
            <Card style={styles.card}>
              {r.infoHtml ? (
                <RichText html={r.infoHtml} />
              ) : (
                <AppText variant="body" color={colors.textSecondary}>
                  Er staat nog geen informatie klaar.
                </AppText>
              )}
            </Card>
          </>
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 12, paddingBottom: 24 },
  card: { padding: 16 },
});
