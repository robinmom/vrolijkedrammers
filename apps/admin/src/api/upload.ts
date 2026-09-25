import type { AuthService } from '../auth/auth';
import { ApiError, type Problem } from './errors';

/** Multipart-upload met het access token (openapi-fetch serialiseert geen FormData voor ons). */
export async function upload(auth: AuthService, method: 'POST' | 'PUT', path: string, form: FormData): Promise<void> {
  const response = await fetch(path, {
    method,
    body: form,
    headers: { Authorization: `Bearer ${await auth.getAccessToken()}` },
  });
  if (!response.ok) {
    let problem: Problem = { status: response.status };
    try {
      problem = { ...(await response.json()), status: response.status } as Problem;
    } catch {
      // Geen JSON.
    }
    throw new ApiError(problem);
  }
}
