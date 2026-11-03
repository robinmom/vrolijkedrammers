import { api } from '../api/client';
import { countUnsentLocalData } from '../auth/localData';
import { clearLocalSession, getInstallationId } from '../auth/session';
import { showAlert } from '../lib/alert';
import { flushQueue } from './offlineScan';

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

/**
 * Voor het uitloggen: hoeveel offline scans en jurypunten de server nog niet heeft (uitloggen wist ze). Scans worden
 * eerst nog verstuurd als dat lukt; jurypunten verstuurt het jureerscherm zelf zodra er netwerk is.
 */
export async function unsentBeforeSignOut(): Promise<number> {
  await flushQueue().catch(() => undefined);
  return countUnsentLocalData();
}

/**
 * Uitloggen met de waarschuwing erbij: staan er nog onverstuurde scans of jurypunten op dit toestel, dan wist uitloggen
 * ze. Wordt gebruikt vanuit Mijn account én vanuit de jury-modus, waar uitloggen de enige andere knop is.
 */
export async function confirmSignOut(onSignedOut: () => void): Promise<void> {
  const unsent = await unsentBeforeSignOut();
  const leave = () => void signOut().then(onSignedOut);
  if (unsent === 0) {
    leave();
    return;
  }

  showAlert(
    'Nog niet verstuurd',
    `Op deze telefoon staan nog ${unsent} ${unsent === 1 ? 'scan of jurypunt' : 'scans of jurypunten'} die nog niet zijn verstuurd. Uitloggen wist ze. Open eerst met internet het scan- of jureerscherm, zodat ze worden verstuurd.`,
    [
      { text: 'Annuleren', style: 'cancel' },
      { text: 'Toch uitloggen', style: 'destructive', onPress: leave },
    ],
  );
}
