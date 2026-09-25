import { QueryClientProvider } from '@tanstack/react-query';
import { createQueryClient } from '../api/QueryProvider';
import { renderRouter } from 'expo-router/testing-library';
import type { ComponentType, ReactNode } from 'react';
import { ThemeProvider, type ThemeMode } from '../theme/ThemeProvider';

type Body = unknown | { status: number; body?: unknown };

/**
 * Laat `fetch` antwoorden per API-pad (zonder querystring). Onbekende paden geven 404, zodat een test
 * nooit per ongeluk het netwerk op gaat. Geeft de lijst met aangeroepen paden terug.
 */
export function mockApi(routes: Record<string, Body>): string[] {
  const calls: string[] = [];
  (globalThis.fetch as jest.Mock).mockImplementation(async (input: Request | string) => {
    const path = new URL(typeof input === 'string' ? input : input.url).pathname;
    calls.push(path);
    const route = routes[path];
    const { status, body } =
      route && typeof route === 'object' && 'status' in route ? (route as { status: number; body?: unknown }) : { status: route === undefined ? 404 : 200, body: route };
    return new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
  });
  return calls;
}

/** Dezelfde instellingen als de app (staleTime e.d.), alleen zonder nieuwe pogingen, zodat fouten direct zichtbaar zijn. */
export function createTestQueryClient() {
  const client = createQueryClient();
  client.setDefaultOptions({ queries: { ...client.getDefaultOptions().queries, retry: false, gcTime: Infinity } });
  return client;
}

/** Rendert schermen binnen Expo Router met thema en een verse querycache (zonder persistentie). */
export async function renderApp(routes: Record<string, ComponentType>, initialUrl: string, mode: ThemeMode = 'light') {
  const client = createTestQueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <ThemeProvider mode={mode}>
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    </ThemeProvider>
  );
  return await renderRouter(routes, { initialUrl, wrapper });
}
