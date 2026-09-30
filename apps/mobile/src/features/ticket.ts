import type { components } from '@drammers/api-client';
import * as SecureStore from 'expo-secure-store';
import { DeviceKey } from '../../modules/device-key';
import { api } from '../api/client';
import { base45, fromBase64, QR_VERSION, QR_VERSION_TOKENS, signedPayload, toBase64, unsignedPayload } from './deviceQr';

export type MyTicket = components['schemas']['MyTicket'];

/** Mijn QR (ingang) of de munten-QR (kassa, fase 19b). */
export type QrPurpose = 'Access' | 'Tokens';

/** Sleutel in de Secure Enclave/Keystore voor het ledenticket (ADR-005). */
export const TICKET_KEY_ALIAS = 'dvd-ticket-key';
/** De app maakt elke 30 seconden een nieuwe code; een code is 45 seconden geldig. */
export const REFRESH_SECONDS = 30;
const VALID_FOR = 45;

const STORE: SecureStore.SecureStoreOptions = { keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY };
const TICKET_CACHE = 'dvd.ticket.v1';
const KEY_STATE = 'dvd.ticketkey.v1';
/** Kinderen waarvan dit toestel een ticket bewaart (fase 17), zodat uitloggen ook die gegevens wist. */
const CHILD_CACHES = 'dvd.ticket.children.v1';

/** Eigen ticket, of dat van een kind waarvan de gebruiker ouder/verzorger is (fase 17). */
const cacheKey = (childId?: string) => (childId ? `${TICKET_CACHE}.${childId}` : TICKET_CACHE);

/** Wat de app nodig heeft om zonder internet een code te maken (geen persoonsgegevens behalve de naam op het scherm). */
export interface CachedTicket {
  publicRef: string;
  credentialVersion: number;
  deviceShortId: string;
  holderName: string | null;
  carnivalYearName: string | null;
  validFrom: string | null;
  validTo: string | null;
}

export const hasHardwareKey = () => DeviceKey !== null;

export async function saveTicket(ticket: MyTicket, childId?: string): Promise<void> {
  if (ticket.publicRef && ticket.deviceShortId && ticket.boundToThisDevice && ticket.deviceHasHardwareKey) {
    const cached: CachedTicket = {
      publicRef: ticket.publicRef,
      credentialVersion: ticket.credentialVersion,
      deviceShortId: ticket.deviceShortId,
      holderName: ticket.holderName,
      carnivalYearName: ticket.carnivalYearName,
      validFrom: ticket.validFrom,
      validTo: ticket.validTo,
    };
    await SecureStore.setItemAsync(cacheKey(childId), JSON.stringify(cached), STORE);
    if (childId) {
      const known = await childCaches();
      if (!known.includes(childId)) {
        await SecureStore.setItemAsync(CHILD_CACHES, JSON.stringify([...known, childId]), STORE);
      }
    }
  } else {
    await SecureStore.deleteItemAsync(cacheKey(childId), STORE);
  }
}

async function childCaches(): Promise<string[]> {
  const raw = await SecureStore.getItemAsync(CHILD_CACHES, STORE).catch(() => null);
  return raw ? (JSON.parse(raw) as string[]) : [];
}

export async function loadTicket(childId?: string): Promise<CachedTicket | null> {
  const raw = await SecureStore.getItemAsync(cacheKey(childId), STORE).catch(() => null);
  return raw ? (JSON.parse(raw) as CachedTicket) : null;
}

/** Bij uitloggen: ticketgegevens en sleutelstatus van dit toestel vergeten. */
export async function forgetTicket(): Promise<void> {
  const children = await childCaches();
  await Promise.all([
    SecureStore.deleteItemAsync(TICKET_CACHE, STORE),
    SecureStore.deleteItemAsync(KEY_STATE, STORE),
    SecureStore.deleteItemAsync(CHILD_CACHES, STORE),
    ...children.map((id) => SecureStore.deleteItemAsync(cacheKey(id), STORE)),
  ]).catch(() => undefined);
}

/**
 * Zorgt dat dit toestel een hardwaresleutel heeft die de API kent. `false` zonder de native module (Expo Go, web):
 * dan gebruikt de app de door de server ondertekende code (fallback, OQ-68).
 */
async function ensureDeviceKey(force = false): Promise<boolean> {
  if (!DeviceKey) {
    return false;
  }
  const raw = force ? null : await SecureStore.getItemAsync(KEY_STATE, STORE).catch(() => null);
  if (raw && (JSON.parse(raw) as { registered: boolean }).registered) {
    return true;
  }
  let publicKey = force ? null : await DeviceKey.getPublicKeyAsync(TICKET_KEY_ALIAS);
  let securityLevel = raw ? (JSON.parse(raw) as { level?: string }).level : undefined;
  if (!publicKey || !securityLevel) {
    if (force) {
      await DeviceKey.deleteKeyAsync(TICKET_KEY_ALIAS).catch(() => undefined);
    }
    const generated = await DeviceKey.generateKeyAsync(TICKET_KEY_ALIAS, null);
    publicKey = generated.publicKey;
    securityLevel = generated.securityLevel;
    await SecureStore.setItemAsync(KEY_STATE, JSON.stringify({ registered: false, level: securityLevel }), STORE);
  }
  const { response } = await api.PUT('/api/v1/me/devices/current/key', { body: { publicKey, securityLevel } });
  if (!response.ok) {
    throw new Error(`Sleutel registreren mislukt (${response.status})`);
  }
  await SecureStore.setItemAsync(KEY_STATE, JSON.stringify({ registered: true, level: securityLevel }), STORE);
  return true;
}

/**
 * Kent de API de hardwaresleutel van dit toestel niet (meer), bijvoorbeeld na opnieuw inloggen, dan registreert de
 * app hem opnieuw. Geeft `true` als er daarna een sleutel is (anders gebruikt de app de servercode).
 */
export async function syncDeviceKey(serverKnowsKey: boolean): Promise<boolean> {
  if (!DeviceKey || serverKnowsKey) {
    return serverKnowsKey;
  }
  await SecureStore.deleteItemAsync(KEY_STATE, STORE).catch(() => undefined);
  return ensureDeviceKey(false).catch(() => false);
}

async function bindOnce(
  force: boolean,
  childId?: string,
): Promise<{ ok: true } | { ok: false; status: number; code?: string; message?: string }> {
  const withKey = await ensureDeviceKey(force).catch(() => false);
  let body: { challenge: string | null; signature: string | null } = { challenge: null, signature: null };
  if (withKey && DeviceKey) {
    const { data } = childId
      ? await api.POST('/api/v1/me/children/{memberId}/ticket/challenge', { params: { path: { memberId: childId } } })
      : await api.POST('/api/v1/me/ticket/challenge');
    if (data) {
      body = { challenge: data.challenge, signature: await DeviceKey.signAsync(TICKET_KEY_ALIAS, data.challenge) };
    }
  }
  const { error, response } = childId
    ? await api.POST('/api/v1/me/children/{memberId}/ticket/bind-device', { params: { path: { memberId: childId } }, body })
    : await api.POST('/api/v1/me/ticket/bind-device', { body });
  if (response.ok) {
    return { ok: true };
  }
  const problem = error as { code?: string; detail?: string } | undefined;
  return { ok: false, status: response.status, code: problem?.code, message: problem?.detail };
}

/**
 * Koppelt het ticket aan dit toestel, met proof-of-possession als er een hardwaresleutel is. Kent de API de sleutel
 * niet (meer), bijvoorbeeld na opnieuw inloggen, dan eenmalig een nieuwe sleutel maken en opnieuw proberen. Met
 * `childId` het ticket van een kind op de telefoon van de ouder (fase 17).
 */
export async function bindThisDevice(childId?: string): Promise<{ ok: true } | { ok: false; message: string }> {
  let result = await bindOnce(false, childId);
  if (!result.ok && result.code === 'DEVICE_KEY_INVALID') {
    result = await bindOnce(true, childId);
  }
  return result.ok
    ? result
    : { ok: false, message: result.message ?? 'Koppelen aan dit toestel lukt nu niet. Probeer het opnieuw.' };
}

/** Code met de hardwaresleutel van dit toestel (werkt zonder internet). */
export async function deviceCode(
  ticket: CachedTicket,
  now: Date,
  purpose: QrPurpose = 'Access',
  /** Munten-QR: de referentie van de muntenbestelling in plaats van die van het ledenticket. */
  reference?: string,
): Promise<{ code: string; issuedAt: number }> {
  const issuedAt = Math.floor(now.getTime() / 1000);
  const unsigned = unsignedPayload({
    ref: fromBase64(reference ?? ticket.publicRef),
    credentialVersion: ticket.credentialVersion,
    deviceId: fromBase64(ticket.deviceShortId),
    issuedAt,
    validFor: VALID_FOR,
    version: purpose === 'Tokens' ? QR_VERSION_TOKENS : QR_VERSION,
  });
  const signature = await DeviceKey!.signAsync(TICKET_KEY_ALIAS, toBase64(unsigned));
  return { code: base45(signedPayload(unsigned, fromBase64(signature))), issuedAt };
}

/** Door de server ondertekende code voor toestellen zonder hardwaresleutel (alleen met internet). */
export async function serverCode(
  childId?: string,
  purpose: QrPurpose = 'Access',
  orderTicketId?: string,
): Promise<{ code: string; issuedAt: number } | null> {
  const { data } = childId
    ? await api.GET('/api/v1/me/children/{memberId}/ticket/code', { params: { path: { memberId: childId } } })
    : await api.GET('/api/v1/me/ticket/code', { params: { query: { purpose, orderTicketId } } });
  return data ? { code: data.code, issuedAt: data.issuedAt } : null;
}

const day = new Intl.DateTimeFormat('nl-NL', { weekday: 'short', day: 'numeric', timeZone: 'Europe/Amsterdam' });
const dayMonthYear = new Intl.DateTimeFormat('nl-NL', {
  weekday: 'short',
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  timeZone: 'Europe/Amsterdam',
});

/** "za 13 t/m di 16 februari 2027"; het einde is 06:00 de dag na de laatste carnavalsdag. */
export function validityText(from: string | null, to: string | null): string {
  if (!from || !to) return '';
  const last = new Date(new Date(to).getTime() - 24 * 60 * 60 * 1000);
  return `${day.format(new Date(from))} t/m ${dayMonthYear.format(last)}`;
}
