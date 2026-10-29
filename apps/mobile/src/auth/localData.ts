import AsyncStorage from '@react-native-async-storage/async-storage';
import * as SecureStore from 'expo-secure-store';
import { removeAllPhotoFiles } from '../features/juryPhotos';
import { storageKeys } from '../lib/storageKeys';

/**
 * Gegevens van de ingelogde gebruiker op dit toestel (ticket, jurypunten, offline scans, querycache). Bij uitloggen
 * wist `clearLocalSession` (auth/session) ze via {@link clearLocalUserData}, zodat een volgende gebruiker van de
 * telefoon niets van de vorige ziet. Alleen de themakeuze en de kaarten van gasten blijven staan (geen account).
 */

const STORE: SecureStore.SecureStoreOptions = { keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY };

const isJuryKey = (key: string) =>
  key.startsWith(storageKeys.paradeJuryPrefix) ||
  key.startsWith(storageKeys.festivalJuryPrefix) ||
  isPhotoKey(key);

/** Juryfoto's: één item per nog te versturen foto, en de bestanden staan in de documentmap. */
const isPhotoKey = (key: string) => key.startsWith(storageKeys.paradeJuryPhotoPrefix);

async function ticketChildren(): Promise<string[]> {
  const raw = await SecureStore.getItemAsync(storageKeys.ticketChildren, STORE).catch(() => null);
  return raw ? (JSON.parse(raw) as string[]) : [];
}

/** Bij uitloggen: ticketgegevens en sleutelstatus van dit toestel vergeten (ook die van de kinderen). */
export async function forgetTicket(): Promise<void> {
  const children = await ticketChildren().catch(() => [] as string[]);
  await Promise.all([
    SecureStore.deleteItemAsync(storageKeys.ticket, STORE),
    SecureStore.deleteItemAsync(storageKeys.ticketKeyState, STORE),
    SecureStore.deleteItemAsync(storageKeys.ticketChildren, STORE),
    ...children.map((id) => SecureStore.deleteItemAsync(`${storageKeys.ticket}.${id}`, STORE)),
  ]).catch(() => undefined);
}

/** Wist alles van de gebruiker op dit toestel; ook wat nog niet verstuurd is (zie {@link countUnsentLocalData}). */
export async function clearLocalUserData(): Promise<void> {
  const keys = await AsyncStorage.getAllKeys().catch(() => [] as readonly string[]);
  removeAllPhotoFiles();
  await Promise.all([
    forgetTicket(),
    AsyncStorage.multiRemove([storageKeys.queryCache, storageKeys.offlineScans, ...keys.filter(isJuryKey)]).catch(
      () => undefined,
    ),
  ]);
}

/** Aantal offline scans, jurypunten/-opmerkingen en juryfoto's dat de server nog niet heeft (gaat bij uitloggen verloren). */
export async function countUnsentLocalData(): Promise<number> {
  const keys = await AsyncStorage.getAllKeys().catch(() => [] as readonly string[]);
  const entries = await AsyncStorage.multiGet([storageKeys.offlineScans, ...keys.filter(isJuryKey)]).catch(() => []);
  let count = 0;
  for (const [key, raw] of entries) {
    if (!raw) continue;
    try {
      const value = JSON.parse(raw) as unknown;
      if (key === storageKeys.offlineScans || isPhotoKey(key)) {
        // Een wachtrij: alles wat er nog in staat is nog niet verstuurd.
        count += (value as unknown[]).length;
      } else if (Array.isArray(value)) {
        // Optocht: een lijst met punten.
        count += (value as { synced: boolean }[]).filter((s) => !s.synced).length;
      } else {
        // Dansgardefestival: punten en opmerkingen.
        const store = value as { scores?: { synced: boolean }[]; comments?: { synced: boolean }[] };
        count += [...(store.scores ?? []), ...(store.comments ?? [])].filter((s) => !s.synced).length;
      }
    } catch {
      // Onleesbaar: telt niet mee (en wordt bij uitloggen gewoon gewist).
    }
  }
  return count;
}
