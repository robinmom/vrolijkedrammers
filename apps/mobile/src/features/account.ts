import { api } from '../api/client';
import { clearLocalSession, getInstallationId } from '../auth/session';

export const statusLabels: Record<string, string> = {
  Active: 'Actief lid',
  Inactive: 'Niet actief',
  Suspended: 'Geschorst',
  Deceased: 'Overleden',
};

export const groupFunctionLabels: Record<string, string> = { Member: 'Lid', Lead: 'Leiding' };

/** Hoe lang uitloggen hooguit op de API wacht; daarna worden de tokens toch gewist. */
const SIGN_OUT_TIMEOUT_MS = 5_000;

/**
 * Uitloggen: de API stopt de pushberichten naar dit toestel (als dat lukt) en daarna worden de tokens gewist. Het
 * toestel blijft aangemeld, zodat opnieuw inloggen hetzelfde toestel is en gekochte munten blijven werken. Echt
 * afmelden gaat via Mijn apparaten.
 */
export async function signOut(): Promise<void> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), SIGN_OUT_TIMEOUT_MS);
  try {
    await api.POST('/api/v1/me/devices/current/sign-out', {
      headers: { 'x-device-id': await getInstallationId() },
      signal: controller.signal,
    });
  } catch {
    // Offline (of te traag) uitloggen kan altijd; de pushberichten stoppen dan pas bij afmelden in Mijn apparaten.
  } finally {
    clearTimeout(timer);
  }
  await clearLocalSession();
}
