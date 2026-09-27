/**
 * QR-payload volgens ADR-005 (spike OQ-68): vaste binaire layout, ondertekend met de hardwaresleutel en base45-
 * gecodeerd (RFC 9285) zodat de QR de alfanumerieke modus kan gebruiken.
 *
 * | v 1 | ref 16 | cv 2 | did 8 | iat 4 | exp 2 | sig 64 | = 97 bytes
 */
export const QR_VERSION = 1;
export const UNSIGNED_LENGTH = 33;
export const SIGNATURE_LENGTH = 64;

export interface QrFields {
  ref: Uint8Array;
  credentialVersion: number;
  deviceId: Uint8Array;
  /** Unix-tijd in seconden. */
  issuedAt: number;
  /** Geldigheid in seconden (standaard 45). */
  validFor: number;
}

/** De ondertekende velden (alles vóór `sig`). */
export function unsignedPayload(fields: QrFields): Uint8Array {
  if (fields.ref.length !== 16 || fields.deviceId.length !== 8) {
    throw new Error('ref moet 16 bytes zijn en did 8 bytes.');
  }
  const bytes = new Uint8Array(UNSIGNED_LENGTH);
  const view = new DataView(bytes.buffer);
  view.setUint8(0, QR_VERSION);
  bytes.set(fields.ref, 1);
  view.setUint16(17, fields.credentialVersion);
  bytes.set(fields.deviceId, 19);
  view.setUint32(27, fields.issuedAt);
  view.setUint16(31, fields.validFor);
  return bytes;
}

export function signedPayload(unsigned: Uint8Array, signature: Uint8Array): Uint8Array {
  if (unsigned.length !== UNSIGNED_LENGTH || signature.length !== SIGNATURE_LENGTH) {
    throw new Error('Onverwachte lengte van payload of handtekening.');
  }
  const bytes = new Uint8Array(UNSIGNED_LENGTH + SIGNATURE_LENGTH);
  bytes.set(unsigned, 0);
  bytes.set(signature, UNSIGNED_LENGTH);
  return bytes;
}

const BASE45 = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:';

/** Base45 (RFC 9285): per 2 bytes 3 tekens, een laatste losse byte 2 tekens. */
export function base45(bytes: Uint8Array): string {
  let out = '';
  for (let i = 0; i < bytes.length; i += 2) {
    if (i + 1 < bytes.length) {
      let n = bytes[i]! * 256 + bytes[i + 1]!;
      const c = n % 45;
      n = (n - c) / 45;
      out += BASE45[c]! + BASE45[n % 45]! + BASE45[Math.floor(n / 45)]!;
    } else {
      const n = bytes[i]!;
      out += BASE45[n % 45]! + BASE45[Math.floor(n / 45)]!;
    }
  }
  return out;
}

/** Kleinste QR-versie (foutcorrectie M, alfanumeriek) voor zoveel tekens; ISO/IEC 18004 tabel 7. */
export function qrVersionForAlphanumeric(length: number): number {
  const capacityM = [20, 38, 61, 90, 122, 154, 178, 221, 262, 311];
  const index = capacityM.findIndex((capacity) => length <= capacity);
  if (index < 0) {
    throw new Error('Te lang voor QR-versie 10.');
  }
  return index + 1;
}

export function toBase64(bytes: Uint8Array): string {
  let binary = '';
  bytes.forEach((b) => (binary += String.fromCharCode(b)));
  return btoa(binary);
}

export function fromBase64(value: string): Uint8Array {
  const binary = atob(value);
  return Uint8Array.from(binary, (c) => c.charCodeAt(0));
}
