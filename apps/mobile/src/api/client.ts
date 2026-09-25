import { createApiClient, type components } from '@drammers/api-client';

type Schemas = components['schemas'];
export type CarnivalYear = Schemas['CarnivalYearResponse'];
export type AppConfig = Schemas['AppConfigResponse'];
export type EventCategory = Schemas['EventCategoryResponse'];
export type EventSummary = Schemas['EventSummaryResponse'];
export type EventDetail = Schemas['EventDetailResponse'];
export type NewsSummary = Schemas['NewsSummaryResponse'];
export type NewsDetail = Schemas['NewsDetailResponse'];
export type PhotoAlbum = Schemas['PhotoAlbumResponse'];
export type Photo = Schemas['PhotoResponse'];

/**
 * Basisadres van de API. `EXPO_PUBLIC_API_URL` wordt bij het bundelen ingevuld (per EAS-profiel);
 * zonder waarde gebruikt de app de Dev-omgeving. Alleen HTTPS; er staan geen secrets in de bundle.
 */
export const apiBaseUrl = process.env.EXPO_PUBLIC_API_URL ?? 'https://app-dvd-api-dev.azurewebsites.net';

/** Fout van de API met de HTTP-status; schermen tonen nooit technische details (docs/16 §6). */
export class ApiError extends Error {
  constructor(readonly status: number) {
    super(`API-fout ${status}`);
  }
}

export const api = createApiClient(apiBaseUrl);

/** Pakt het resultaat van een openapi-fetch-aanroep uit; elke niet-2xx wordt een {@link ApiError}. */
export async function unwrap<T>(call: Promise<{ data?: T; response: Response }>): Promise<T> {
  const { data, response } = await call;
  if (!response.ok || data === undefined) {
    throw new ApiError(response.status);
  }
  return data;
}
