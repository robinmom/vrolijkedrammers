import type { ApiClient } from '@drammers/api-client';
import { createContext, useContext, type ReactNode } from 'react';

const ApiContext = createContext<ApiClient | null>(null);

export function ApiProvider({ api, children }: { api: ApiClient; children: ReactNode }) {
  return <ApiContext.Provider value={api}>{children}</ApiContext.Provider>;
}

export function useApi(): ApiClient {
  const api = useContext(ApiContext);
  if (!api) {
    throw new Error('useApi buiten ApiProvider');
  }
  return api;
}
