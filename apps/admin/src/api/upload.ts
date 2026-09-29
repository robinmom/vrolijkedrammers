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

/** Multipart-upload die een JSON-antwoord teruggeeft (bijv. het voorbeeld van een import). */
export async function uploadJson<T>(auth: AuthService, path: string, form: FormData): Promise<T> {
  const response = await fetch(path, {
    method: 'POST',
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
  return (await response.json()) as T;
}
