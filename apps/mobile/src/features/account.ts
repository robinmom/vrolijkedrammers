import { api } from '../api/client';
import { countUnsentLocalData } from '../auth/localData';
import { clearLocalSession, getInstallationId } from '../auth/session';
import { flushQueue } from './offlineScan';

export const statusLabels: Record<string, string> = {
  Active: 'Actief lid',
  Inactive: 'Niet actief',
  Suspended: 'Geschorst',
  Deceased: 'Overleden',
};

export const groupFunctionLabels: Record<string, string> = { Member: 'Lid', Lead: 'Leiding' };

/**
 * Uitloggen: de API stopt de pushberichten naar dit toestel (als dat lukt) en daarna worden de tokens gewist. Het
 * toestel blijft aangemeld, zodat opnieuw inloggen hetzelfde toestel is en gekochte munten blijven werken. Echt
 * afmelden gaat via Mijn apparaten.
 */
export async function signOut(): Promise<void> {
  try {
    await api.POST('/api/v1/me/devices/current/sign-out', { headers: { 'x-device-id': await getInstallationId() } });
  } catch {
    // Offline uitloggen kan altijd; de pushberichten stoppen dan pas bij afmelden in Mijn apparaten.
  }
  await clearLocalSession();
}

/**
 * Voor het uitloggen: hoeveel offline scans en jurypunten de server nog niet heeft (uitloggen wist ze). Scans worden
 * eerst nog verstuurd als dat lukt; jurypunten verstuurt het jureerscherm zelf zodra er netwerk is.
 */
export async function unsentBeforeSignOut(): Promise<number> {
  await flushQueue().catch(() => undefined);
  return countUnsentLocalData();
}
