import { useQueryClient, type QueryKey } from '@tanstack/react-query';
import { useCallback, useState } from 'react';

/** Pull-to-refresh voor de queries van een scherm; de spinner loopt tot alle queries klaar zijn. */
export function useRefresh(keys: QueryKey[]) {
  const client = useQueryClient();
  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    try {
      await Promise.all(keys.map((queryKey) => client.refetchQueries({ queryKey })));
    } finally {
      setRefreshing(false);
    }
  }, [client, keys]);
  return { refreshing, onRefresh };
}
