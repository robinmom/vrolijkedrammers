import { useQueryClient, type QueryKey } from '@tanstack/react-query';
import { useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useRef, useState } from 'react';

/**
 * Verversen van de queries van een scherm:
 * - pull-to-refresh haalt alles opnieuw op (de spinner loopt tot alle queries klaar zijn);
 * - bij het openen van het scherm (ook bij wisselen van tab) wordt verouderde data op de achtergrond ververst.
 *   Tabs blijven gemount, dus zonder dit zou nieuw gepubliceerde content pas na pull-to-refresh verschijnen.
 */
export function useRefresh(queryKeys: QueryKey[]) {
  const client = useQueryClient();
  // Schermen geven elke render een nieuwe array mee; op inhoud vergelijken voorkomt dat de focus-effect steeds opnieuw draait.
  const signature = JSON.stringify(queryKeys);
  const keys = useMemo(() => JSON.parse(signature) as QueryKey[], [signature]);
  const [refreshing, setRefreshing] = useState(false);
  const firstFocus = useRef(true);

  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    try {
      await Promise.all(keys.map((queryKey) => client.refetchQueries({ queryKey })));
    } finally {
      setRefreshing(false);
    }
  }, [client, keys]);

  useFocusEffect(
    useCallback(() => {
      // De eerste keer laadt de query zelf al; daarna alleen verouderde data opnieuw ophalen.
      if (firstFocus.current) {
        firstFocus.current = false;
        return;
      }
      for (const queryKey of keys) {
        void client.refetchQueries({ queryKey, stale: true, type: 'active' });
      }
    }, [client, keys]),
  );

  return { refreshing, onRefresh };
}
