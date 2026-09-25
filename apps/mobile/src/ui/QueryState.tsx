import type { UseQueryResult } from '@tanstack/react-query';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { ApiError } from '../api/client';
import { useTheme } from '../theme/ThemeProvider';
import { EmptyState, ErrorState } from './StateMessage';

/**
 * Laad- en foutweergave voor een query. Geeft `null` terug zodra er data is, ook als die uit de offline cache komt;
 * een mislukte verversing met bestaande data toont dus gewoon de laatste content (de offline-banner meldt de rest).
 */
export function QueryState({ query, notFoundTitle }: { query: UseQueryResult<unknown>; notFoundTitle?: string }) {
  const { colors } = useTheme();
  if (query.data !== undefined) {
    return null;
  }
  if (query.isPending && query.fetchStatus === 'fetching') {
    return (
      <View style={styles.loading}>
        <ActivityIndicator color={colors.accentText} accessibilityLabel="Laden" />
      </View>
    );
  }
  if (query.error instanceof ApiError && query.error.status === 404 && notFoundTitle) {
    return <EmptyState title={notFoundTitle} message="Dit onderdeel bestaat niet (meer) of is niet openbaar." />;
  }
  if (query.isPending) {
    // Offline zonder cache pauzeert TanStack Query de query tot er weer netwerk is; een uitgeschakelde query toont niets.
    return query.fetchStatus === 'paused' ? (
      <ErrorState title="Geen verbinding" message="Deze gegevens zijn nog niet eerder geladen. Probeer het opnieuw als je online bent." />
    ) : null;
  }
  return <ErrorState action={{ label: 'Opnieuw proberen', onPress: () => query.refetch() }} />;
}

const styles = StyleSheet.create({
  loading: { paddingVertical: 40, alignItems: 'center' },
});
