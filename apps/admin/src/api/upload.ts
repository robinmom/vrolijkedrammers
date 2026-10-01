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

/**
 * Multipart-upload met voortgang (fase 21b, bulk-upload van foto's): XMLHttpRequest meldt, anders dan fetch, hoeveel er
 * al verstuurd is. `onProgress` krijgt een getal van 0 tot 1.
 */
export async function uploadWithProgress(
  auth: AuthService,
  path: string,
  form: FormData,
  onProgress: (fraction: number) => void,
): Promise<void> {
  const token = await auth.getAccessToken();
  await new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', path);
    xhr.setRequestHeader('Authorization', `Bearer ${token}`);
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) onProgress(e.loaded / e.total);
    };
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        onProgress(1);
        resolve();
        return;
      }
      let problem: Problem = { status: xhr.status };
      try {
        problem = { ...(JSON.parse(xhr.responseText) as Problem), status: xhr.status };
      } catch {
        // Geen JSON.
      }
      reject(new ApiError(problem));
    };
    xhr.onerror = () => reject(new ApiError({ status: 0, title: 'Geen verbinding met de server.' }));
    xhr.send(form);
  });
}
