import createClient from 'openapi-fetch';
import type { paths } from './schema';

export type { components, paths } from './schema';

/**
 * Maakt een getypte client voor de Drammers API. Het contract komt uit het door de build
 * gegenereerde OpenAPI-document; tokens worden per request via `headers` of middleware toegevoegd.
 */
export function createApiClient(baseUrl: string) {
  return createClient<paths>({ baseUrl });
}

export type ApiClient = ReturnType<typeof createApiClient>;
