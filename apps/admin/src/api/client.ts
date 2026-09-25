import { createApiClient, type ApiClient } from '@drammers/api-client';
import type { AuthService } from '../auth/auth';
import { ApiError, type Problem } from './errors';

/** API-client met het access token in elke request; fouten worden {@link ApiError}s met de ProblemDetails. */
export function createAdminApi(auth: AuthService): ApiClient {
  const client = createApiClient(window.location.origin);
  client.use({
    async onRequest({ request }) {
      request.headers.set('Authorization', `Bearer ${await auth.getAccessToken()}`);
      return request;
    },
    async onResponse({ response }) {
      if (!response.ok) {
        let problem: Problem = { status: response.status };
        try {
          problem = { ...(await response.clone().json()), status: response.status } as Problem;
        } catch {
          // Geen JSON: standaardtekst per status.
        }
        throw new ApiError(problem);
      }
      return response;
    },
  });
  return client;
}
