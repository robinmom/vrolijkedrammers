import AsyncStorage from '@react-native-async-storage/async-storage';
import { randomUUID } from 'expo-crypto';
import { Directory, File, Paths, UploadType } from 'expo-file-system';
import { apiBaseUrl } from '../api/client';
import { getAccessToken, getInstallationId } from '../auth/session';
import { storageKeys } from '../lib/storageKeys';

/**
 * Juryfoto's (fase 22e): één foto per wagen of groep, gemaakt of gekozen tijdens de optocht. Buiten op de route is er
 * vaak geen bereik, dus de foto gaat eerst naar een eigen map op het toestel en wordt daarna in de achtergrond
 * verstuurd — net als de offline scans bij de ingang. Pas als de server hem heeft, verdwijnt het bestand.
 */

/** Een foto die nog verstuurd moet worden; `name` is het bestand in {@link folder}. */
export interface PendingPhoto {
  registrationId: string;
  name: string;
  queuedAt: string;
}

const queueKey = (userId: string, paradeId: string) => `${storageKeys.paradeJuryPhotoPrefix}${userId}:${paradeId}`;

/**
 * Waarom de laatste poging misliep, zodat de app geen "wacht op internet" toont terwijl er iets anders aan de hand is.
 * Bewust niet in de opslag: bij een volgende poging is hij toch achterhaald.
 */
let lastError: string | null = null;

export const lastPhotoError = () => lastError;

/** Eigen map in de documentmap: die wordt niet door het systeem opgeruimd als de opslag vol raakt. */
const folder = () => new Directory(Paths.document, 'jury-photos');

function ensureFolder(): Directory {
  const directory = folder();
  if (!directory.exists) {
    directory.create({ intermediates: true });
  }
  return directory;
}

function removeFile(name: string): void {
  try {
    const file = new File(folder(), name);
    if (file.exists) {
      file.delete();
    }
  } catch {
    // Weg is weg; een achtergebleven bestand mag het versturen niet blokkeren.
  }
}

async function readQueue(userId: string, paradeId: string): Promise<PendingPhoto[]> {
  const raw = await AsyncStorage.getItem(queueKey(userId, paradeId)).catch(() => null);
  return raw ? (JSON.parse(raw) as PendingPhoto[]) : [];
}

const writeQueue = (userId: string, paradeId: string, queue: PendingPhoto[]) =>
  AsyncStorage.setItem(queueKey(userId, paradeId), JSON.stringify(queue));

/**
 * Zet een gekozen of gemaakte foto in de wachtrij. Eén per inzending: een eerdere foto die nog niet verstuurd was
 * vervalt, inclusief het bestand.
 */
export async function queuePhoto(userId: string, paradeId: string, registrationId: string, sourceUri: string): Promise<number> {
  const directory = ensureFolder();
  // Een echt unieke naam: met een tijdstempel kunnen twee foto's binnen dezelfde milliseconde dezelfde naam krijgen, en
  // dan gooit het opruimen van de vorige juist het net gekopieerde bestand weg.
  const name = `${registrationId}-${randomUUID()}.jpg`;
  new File(sourceUri).copy(new File(directory, name));
  const queue = await readQueue(userId, paradeId);
  for (const old of queue.filter((p) => p.registrationId === registrationId)) {
    removeFile(old.name);
  }
  const next = [...queue.filter((p) => p.registrationId !== registrationId), { registrationId, name, queuedAt: new Date().toISOString() }];
  await writeQueue(userId, paradeId, next);
  return next.length;
}

export const pendingPhotos = async (userId: string, paradeId: string) => (await readQueue(userId, paradeId)).length;

/** De foto die nog op internet wacht voor deze inzending, als die er is (voor de voorvertoning in de app). */
export async function pendingPhotoUri(userId: string, paradeId: string, registrationId: string): Promise<string | null> {
  const pending = (await readQueue(userId, paradeId)).find((p) => p.registrationId === registrationId);
  if (!pending) return null;
  const file = new File(folder(), pending.name);
  return file.exists ? file.uri : null;
}

/**
 * Verstuurt de wachtrij, foto voor foto. Geeft terug hoeveel er nog openstaan. Een mislukte foto blijft staan voor een
 * volgende poging; een foto waarvan het bestand verdwenen is wordt opgeruimd in plaats van eeuwig opnieuw geprobeerd.
 */
export async function flushPhotos(userId: string, paradeId: string): Promise<number> {
  const queue = await readQueue(userId, paradeId);
  if (queue.length === 0) return 0;
  const token = await getAccessToken();
  const deviceId = await getInstallationId();
  const sent: string[] = [];
  lastError = null;
  for (const pending of queue) {
    const file = new File(folder(), pending.name);
    if (!file.exists) {
      sent.push(pending.name);
      continue;
    }
    let status: number;
    try {
      // De eigen uploader van expo-file-system geeft het bestand native door. Een FormData met een `file://`-verwijzing
      // (het patroon van de documentupload) kwam hier niet eens tot een request.
      const result = await file.upload(`${apiBaseUrl}/api/v1/jury/parades/${paradeId}/entries/${pending.registrationId}/photo`, {
        uploadType: UploadType.MULTIPART,
        fieldName: 'file',
        mimeType: 'image/jpeg',
        headers: { authorization: `Bearer ${token ?? ''}`, 'x-device-id': deviceId },
      });
      status = result.status;
    } catch (error) {
      // Geen verbinding of een fout in de upload zelf: laten staan, maar wél onthouden waarom.
      lastError = error instanceof Error ? error.message : 'onbekende fout';
      break;
    }
    if (status >= 200 && status < 300) {
      removeFile(pending.name);
      sent.push(pending.name);
      continue;
    }
    // 4xx anders dan 408/429 komt niet goed door opnieuw te proberen (bijv. al ingediend, of te groot): niet blijven hangen.
    if (status >= 400 && status < 500 && status !== 408 && status !== 429) {
      lastError = `de server weigerde de foto (${status})`;
      removeFile(pending.name);
      sent.push(pending.name);
      continue;
    }
    lastError = `de server gaf een fout (${status})`;
    break;
  }
  const rest = (await readQueue(userId, paradeId)).filter((p) => !sent.includes(p.name));
  await writeQueue(userId, paradeId, rest);
  return rest.length;
}

/** Bij uitloggen: de bestanden van dit toestel weggooien (de sleutels doet `clearLocalUserData`). */
export function removeAllPhotoFiles(): void {
  try {
    const directory = folder();
    if (directory.exists) {
      directory.delete();
    }
  } catch {
    // Niets aan te doen; bij de volgende keuze wordt de map opnieuw aangemaakt.
  }
}
