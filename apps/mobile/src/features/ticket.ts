import type { components } from '@drammers/api-client';
import * as SecureStore from 'expo-secure-store';
import { DeviceKey } from '../../modules/device-key';
import { api } from '../api/client';
import { base45, fromBase64, signedPayload, toBase64, unsignedPayload } from './deviceQr';

export type MyTicket = components['schemas']['MyTicket'];

/** Sleutel in de Secure Enclave/Keystore voor het ledenticket (ADR-005). */
export const TICKET_KEY_ALIAS = 'dvd-ticket-key';
/** De app maakt elke 30 seconden een nieuwe code; een code is 45 seconden geldig. */
export const REFRESH_SECONDS = 30;
const VALID_FOR = 45;

const STORE: SecureStore.SecureStoreOptions = { keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY };
const TICKET_CACHE = 'dvd.ticket.v1';
const KEY_STATE = 'dvd.ticketkey.v1';

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

export async function saveTicket(ticket: MyTicket): Promise<void> {
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
    await SecureStore.setItemAsync(TICKET_CACHE, JSON.stringify(cached), STORE);
  } else {
    await SecureStore.deleteItemAsync(TICKET_CACHE, STORE);
  }
}

export async function loadTicket(): Promise<CachedTicket | null> {
  const raw = await SecureStore.getItemAsync(TICKET_CACHE, STORE).catch(() => null);
  return raw ? (JSON.parse(raw) as CachedTicket) : null;
}

/** Bij uitloggen: ticketgegevens en sleutelstatus van dit toestel vergeten. */
export async function forgetTicket(): Promise<void> {
  await Promise.all([
    SecureStore.deleteItemAsync(TICKET_CACHE, STORE),
    SecureStore.deleteItemAsync(KEY_STATE, STORE),
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
): Promise<{ ok: true } | { ok: false; status: number; code?: string; message?: string }> {
  const withKey = await ensureDeviceKey(force).catch(() => false);
  let body: { challenge: string | null; signature: string | null } = { challenge: null, signature: null };
  if (withKey && DeviceKey) {
    const { data } = await api.POST('/api/v1/me/ticket/challenge');
    if (data) {
      body = { challenge: data.challenge, signature: await DeviceKey.signAsync(TICKET_KEY_ALIAS, data.challenge) };
    }
  }
  const { error, response } = await api.POST('/api/v1/me/ticket/bind-device', { body });
  if (response.ok) {
    return { ok: true };
  }
  const problem = error as { code?: string; detail?: string } | undefined;
  return { ok: false, status: response.status, code: problem?.code, message: problem?.detail };
}

/**
 * Koppelt het ticket aan dit toestel, met proof-of-possession als er een hardwaresleutel is. Kent de API de sleutel
 * niet (meer), bijvoorbeeld na opnieuw inloggen, dan eenmalig een nieuwe sleutel maken en opnieuw proberen.
 */
export async function bindThisDevice(): Promise<{ ok: true } | { ok: false; message: string }> {
  let result = await bindOnce(false);
  if (!result.ok && result.code === 'DEVICE_KEY_INVALID') {
    result = await bindOnce(true);
  }
  return result.ok
    ? result
    : { ok: false, message: result.message ?? 'Koppelen aan dit toestel lukt nu niet. Probeer het opnieuw.' };
}

/** Code met de hardwaresleutel van dit toestel (werkt zonder internet). */
export async function deviceCode(ticket: CachedTicket, now: Date): Promise<{ code: string; issuedAt: number }> {
  const issuedAt = Math.floor(now.getTime() / 1000);
  const unsigned = unsignedPayload({
    ref: fromBase64(ticket.publicRef),
    credentialVersion: ticket.credentialVersion,
    deviceId: fromBase64(ticket.deviceShortId),
    issuedAt,
    validFor: VALID_FOR,
  });
  const signature = await DeviceKey!.signAsync(TICKET_KEY_ALIAS, toBase64(unsigned));
  return { code: base45(signedPayload(unsigned, fromBase64(signature))), issuedAt };
}

/** Door de server ondertekende code voor toestellen zonder hardwaresleutel (alleen met internet). */
export async function serverCode(): Promise<{ code: string; issuedAt: number } | null> {
  const { data } = await api.GET('/api/v1/me/ticket/code');
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
