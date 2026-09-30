import AsyncStorage from '@react-native-async-storage/async-storage';
import type { components } from '@drammers/api-client';
import { p256 } from '@noble/curves/nist.js';
import { randomUUID } from 'expo-crypto';
import { api } from '../api/client';
import { fromBase64 } from './deviceQr';

export type OfflinePack = components['schemas']['OfflinePack'];
type Outcome = components['schemas']['AccessOutcome'];

/** Offline resultaat zoals het scherm het toont (zelfde vorm als online, zonder scan-id). */
export interface OfflineResult {
  outcome: Outcome;
  title: string;
  message: string;
  holderName: string | null;
}

interface QueuedScan {
  clientScanId: string;
  code: string;
  scannedAt: string;
  localOutcome: Outcome;
}

const QUEUE_KEY = 'dvd.offline-scans.v1';
const CLOCK_SKEW = 90;
const ALPHABET = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:';

/** Base45 (RFC 9285); `null` als het geen geldige base45 is. Geen trim: de spatie is een geldig teken. */
export function base45Decode(text: string): Uint8Array | null {
  if (text.length % 3 === 1) return null;
  const out: number[] = [];
  for (let i = 0; i < text.length; i += 3) {
    const chunk = text.slice(i, i + 3);
    const c = [...chunk].map((ch) => ALPHABET.indexOf(ch));
    if (c.some((x) => x < 0)) return null;
    if (c.length === 3) {
      const n = c[0]! + c[1]! * 45 + c[2]! * 2025;
      if (n > 0xffff) return null;
      out.push(n >> 8, n & 0xff);
    } else {
      const n = c[0]! + c[1]! * 45;
      if (n > 0xff) return null;
      out.push(n);
    }
  }
  return Uint8Array.from(out);
}

const equal = (a: Uint8Array, b: Uint8Array) => a.length === b.length && a.every((v, i) => v === b[i]);

/** SubjectPublicKeyInfo (EC P-256) → het ongecomprimeerde punt (laatste 65 bytes). */
const rawPoint = (spki: string) => fromBase64(spki).slice(-65);

/**
 * ECDSA P-256/SHA-256 met r‖s. Toestellen en .NET normaliseren S niet, dus ook "high-S"-handtekeningen accepteren.
 */
export function verifySignature(spki: string, data: Uint8Array, signature: Uint8Array): boolean {
  try {
    return p256.verify(signature, data, rawPoint(spki), { lowS: false });
  } catch {
    return false;
  }
}

/**
 * Offline controle (fase 15, lichte variant) met de controlelijst in het geheugen: dezelfde regels als de server
 * (ADR-005 §5), plus "al eerder gescand op dit toestel" voor deze sessie. Wat de server later anders beoordeelt,
 * verschijnt als offline-conflict in de toegangslog.
 */
export function checkOffline(pack: OfflinePack, code: string, now: Date, seenHere: Map<string, Date>): OfflineResult {
  const refuse = (message: string, holderName: string | null = null): OfflineResult => ({
    outcome: 'Refused',
    title: 'Geen toegang',
    message,
    holderName,
  });
  const bytes = base45Decode(code);
  // Gekochte kaarten (versie 3) alleen online: de server zet de QR in één keer op gebruikt (fase 19c).
  if (bytes?.length === 97 && bytes[0] === 3)
    return refuse('Gekochte kaart: alleen online te scannen. Probeer het opnieuw zodra er internet is.');
  if (bytes?.length === 97 && (bytes[0] === 4 || bytes[0] === 5))
    return refuse('Dit is de munten-QR. Vraag om Mijn QR voor de ingang.');
  if (!bytes || bytes.length !== 97 || (bytes[0] !== 1 && bytes[0] !== 2))
    return refuse('Geen geldige QR-code van De Vrolijke Drammers.');
  const ref = bytes.slice(1, 17);
  const view = new DataView(bytes.buffer);
  const cv = view.getUint16(17);
  const did = bytes.slice(19, 27);
  const iat = view.getUint32(27);
  const exp = view.getUint16(31);
  const ticket = pack.tickets.find((t) => equal(fromBase64(t.ref), ref));
  if (!ticket) return refuse('Onbekend ticket.');
  if (ticket.blocked) return refuse('Ticket geblokkeerd.', ticket.holderName);
  if (!ticket.membershipActive) return refuse('Geen actief lidmaatschap.', ticket.holderName);
  const from = pack.validFrom ? new Date(pack.validFrom) : null;
  const to = pack.validTo ? new Date(pack.validTo) : null;
  if (!from || !to || now < from || now > to)
    return refuse('Ticket is nu niet geldig (geen toegangsmoment).', ticket.holderName);
  if (cv !== ticket.credentialVersion) return refuse('Oude code: het ticket is opnieuw uitgegeven.', ticket.holderName);
  if (!ticket.deviceShortId || !equal(fromBase64(ticket.deviceShortId), did)) {
    return refuse('Code van een ander toestel dan waaraan het ticket gekoppeld is.', ticket.holderName);
  }
  const unsigned = bytes.slice(0, 33);
  const signature = bytes.slice(33);
  const signed =
    bytes[0] === 1
      ? Boolean(ticket.devicePublicKey) && verifySignature(ticket.devicePublicKey!, unsigned, signature)
      : pack.serverKeys.some((k) => verifySignature(k, unsigned, signature));
  if (!signed) return refuse('Ongeldige handtekening: de code is nagemaakt of gewijzigd.', ticket.holderName);
  const seconds = now.getTime() / 1000;
  if (seconds < iat - CLOCK_SKEW || seconds > iat + exp + CLOCK_SKEW) {
    return refuse('Verlopen code (bijvoorbeeld een screenshot). Vraag om de live code.', ticket.holderName);
  }
  const earlier = seenHere.get(ticket.ref);
  if (earlier) {
    const time = earlier.toLocaleTimeString('nl-NL', {
      hour: '2-digit',
      minute: '2-digit',
      timeZone: 'Europe/Amsterdam',
    });
    return {
      outcome: 'AdmittedAgain',
      title: 'Toegang geldig',
      message: `Al eerder gescand op dit toestel om ${time}`,
      holderName: ticket.holderName,
    };
  }
  seenHere.set(ticket.ref, now);
  return {
    outcome: 'Admitted',
    title: 'Toegang geldig',
    message: 'Eerste keer vanavond',
    holderName: ticket.holderName,
  };
}

async function readQueue(): Promise<QueuedScan[]> {
  const raw = await AsyncStorage.getItem(QUEUE_KEY).catch(() => null);
  return raw ? (JSON.parse(raw) as QueuedScan[]) : [];
}

/** Wachtrij met offline scans (alleen de code en de tijd, geen ledengegevens); blijft bewaard na herstarten. */
export async function enqueue(code: string, localOutcome: Outcome, at: Date): Promise<number> {
  const queue = await readQueue();
  queue.push({ clientScanId: randomUUID(), code, scannedAt: at.toISOString(), localOutcome });
  await AsyncStorage.setItem(QUEUE_KEY, JSON.stringify(queue));
  return queue.length;
}

export const queueLength = async () => (await readQueue()).length;

/** Verstuurt de wachtrij (max. 500 per keer); verzonden scans gaan eruit. Opnieuw versturen is veilig (idempotent). */
export async function flushQueue(): Promise<number> {
  const queue = await readQueue();
  if (queue.length === 0) return 0;
  const batch = queue.slice(0, 500);
  const { response } = await api.POST('/api/v1/access/offline-scans', { body: { scans: batch } });
  if (!response.ok) throw new Error(`Versturen mislukt (${response.status})`);
  const sent = new Set(batch.map((s) => s.clientScanId));
  const rest = (await readQueue()).filter((s) => !sent.has(s.clientScanId));
  await AsyncStorage.setItem(QUEUE_KEY, JSON.stringify(rest));
  return rest.length;
}
