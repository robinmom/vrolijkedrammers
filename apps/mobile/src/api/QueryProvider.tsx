import AsyncStorage from '@react-native-async-storage/async-storage';
import NetInfo from '@react-native-community/netinfo';
import { createAsyncStoragePersister } from '@tanstack/query-async-storage-persister';
import { focusManager, onlineManager, QueryClient, type Query } from '@tanstack/react-query';
import { PersistQueryClientProvider } from '@tanstack/react-query-persist-client';
import { useEffect, useState, type ReactNode } from 'react';
import { AppState } from 'react-native';
import { getStatus, subscribe, type SessionStatus } from '../auth/session';
import { ApiError } from './client';

const WEEK = 7 * 24 * 60 * 60 * 1000;

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // Na 30 s ververst een scherm zijn data bij openen; ruim binnen de geldigheid van de SAS-links (15 min).
        // De API antwoordt met een ETag (304 als er niets veranderd is), dus vaak verversen is goedkoop.
        staleTime: 30 * 1000,
        // Minstens zo lang als de persistente cache, anders gooit de client offline-data weg.
        gcTime: WEEK,
        // 404 betekent "bestaat niet of geen toegang" (docs/05); opnieuw proberen heeft dan geen zin.
        retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
      },
    },
  });
}

/** Alleen geslaagde queries en nooit persoonlijke gegevens (sleutel `me`) in de offline cache op het toestel. */
export const shouldPersistQuery = (query: Query) => query.state.status === 'success' && query.queryKey[0] !== 'me';

// Netwerkstatus en app-focus doorgeven aan TanStack Query (React Native kent geen window-events).
onlineManager.setEventListener((setOnline) =>
  NetInfo.addEventListener((state) => setOnline(state.isConnected !== false)),
);

/**
 * Querycache die tussen app-starts bewaard blijft: zonder netwerk toont de app de laatst geladen content
 * (fase 6). Persoonlijke gegevens (sleutel `me`) worden niet bewaard en tokens staan er nooit in; bij in- en
 * uitloggen wordt de cache geleegd, omdat ledencontent anders zichtbaar blijft na uitloggen (fase 9).
 */
export function QueryProvider({ children, client }: { children: ReactNode; client?: QueryClient }) {
  const [queryClient] = useState(() => client ?? createQueryClient());
  const [persister] = useState(() =>
    createAsyncStoragePersister({ storage: AsyncStorage, key: 'drammers-query-cache' }),
  );

  useEffect(() => {
    const subscription = AppState.addEventListener('change', (status) => focusManager.setFocused(status === 'active'));
    return () => subscription.remove();
  }, []);

  useEffect(() => {
    let previous: SessionStatus = getStatus();
    return subscribe(() => {
      const next = getStatus();
      // Alleen bij een echte wissel (niet bij het inlezen bij het starten).
      if (previous !== 'loading' && next !== 'loading' && previous !== next) {
        queryClient.clear();
      }
      previous = next;
    });
  }, [queryClient]);

  return (
    <PersistQueryClientProvider
      client={queryClient}
      // De buster invalideert de cache bij een nieuwe appversie met mogelijk ander dataformaat.
      persistOptions={{
        persister,
        maxAge: WEEK,
        buster: 'v1',
        dehydrateOptions: {
          shouldDehydrateQuery: (query) => query.state.status === 'success' && query.queryKey[0] !== 'me',
        },
      }}
    >
      {children}
    </PersistQueryClientProvider>
  );
}
