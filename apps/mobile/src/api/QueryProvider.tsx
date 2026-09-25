import AsyncStorage from '@react-native-async-storage/async-storage';
import NetInfo from '@react-native-community/netinfo';
import { createAsyncStoragePersister } from '@tanstack/query-async-storage-persister';
import { focusManager, onlineManager, QueryClient } from '@tanstack/react-query';
import { PersistQueryClientProvider } from '@tanstack/react-query-persist-client';
import { useEffect, useState, type ReactNode } from 'react';
import { AppState } from 'react-native';
import { ApiError } from './client';

const WEEK = 7 * 24 * 60 * 60 * 1000;

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // Korter dan de geldigheid van de SAS-links (15 min), zodat afbeeldingen online altijd laden.
        staleTime: 5 * 60 * 1000,
        // Minstens zo lang als de persistente cache, anders gooit de client offline-data weg.
        gcTime: WEEK,
        // 404 betekent "bestaat niet of geen toegang" (docs/05); opnieuw proberen heeft dan geen zin.
        retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
      },
    },
  });
}

// Netwerkstatus en app-focus doorgeven aan TanStack Query (React Native kent geen window-events).
onlineManager.setEventListener((setOnline) => NetInfo.addEventListener((state) => setOnline(state.isConnected !== false)));

/**
 * Querycache die tussen app-starts bewaard blijft: zonder netwerk toont de app de laatst geladen content
 * (fase 6). Alleen publieke content; er staat geen persoonsgegeven of token in deze cache.
 */
export function QueryProvider({ children, client }: { children: ReactNode; client?: QueryClient }) {
  const [queryClient] = useState(() => client ?? createQueryClient());
  const [persister] = useState(() => createAsyncStoragePersister({ storage: AsyncStorage, key: 'drammers-query-cache' }));

  useEffect(() => {
    const subscription = AppState.addEventListener('change', (status) => focusManager.setFocused(status === 'active'));
    return () => subscription.remove();
  }, []);

  return (
    <PersistQueryClientProvider
      client={queryClient}
      // De buster invalideert de cache bij een nieuwe appversie met mogelijk ander dataformaat.
      persistOptions={{ persister, maxAge: WEEK, buster: 'v1' }}
    >
      {children}
    </PersistQueryClientProvider>
  );
}
