import { createApiClient, type components } from '@drammers/api-client';
import { clearLocalSession, getAccessToken, getInstallationId, getStatus } from '../auth/session';
import { apiBaseUrl } from './config';

export { apiBaseUrl } from './config';

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
export type Me = Schemas['MeResponse'];
export type MyMember = Schemas['MyMemberResponse'];
export type MyDevice = Schemas['DeviceResponse'];

/** Fout van de API met de HTTP-status; schermen tonen nooit technische details (docs/16 §6). */
export class ApiError extends Error {
  constructor(readonly status: number) {
    super(`API-fout ${status}`);
  }
}

export const api = createApiClient(apiBaseUrl);

/**
 * Ingelogd: elk verzoek krijgt het access-token en de installatie-id (fase 9). Is dit apparaat afgemeld
 * (401 DEVICE_REVOKED) of het account weg (401), dan wist de app de sessie; publieke content blijft werken.
 */
api.use({
  async onRequest({ request }) {
    if (getStatus() !== 'signedIn') {
      return request;
    }
    const token = await getAccessToken();
    if (token) {
      request.headers.set('authorization', `Bearer ${token}`);
      request.headers.set('x-device-id', await getInstallationId());
    }
    return request;
  },
  async onResponse({ response }) {
    if (response.status === 401 && getStatus() === 'signedIn') {
      await clearLocalSession();
    }
    return response;
  },
});

/** Pakt het resultaat van een openapi-fetch-aanroep uit; elke niet-2xx wordt een {@link ApiError}. */
export async function unwrap<T>(call: Promise<{ data?: T; response: Response }>): Promise<T> {
  const { data, response } = await call;
  if (!response.ok || data === undefined) {
    throw new ApiError(response.status);
  }
  return data;
}
