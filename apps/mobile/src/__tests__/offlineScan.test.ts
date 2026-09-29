import AsyncStorage from '@react-native-async-storage/async-storage';
import { p256 } from '@noble/curves/nist.js';
import { base45, fromBase64, signedPayload, toBase64, unsignedPayload } from '../features/deviceQr';
import {
  base45Decode,
  checkOffline,
  enqueue,
  queueLength,
  verifySignature,
  type OfflinePack,
} from '../features/offlineScan';

/** SubjectPublicKeyInfo-kop voor EC P-256 (zoals .NET en de toestellen hem leveren), gevolgd door het punt. */
const SPKI_HEADER = Uint8Array.from(
  '3059301306072a8648ce3d020106082a8648ce3d030107034200'.match(/../g)!.map((h) => parseInt(h, 16)),
);
const spki = (point: Uint8Array) => toBase64(Uint8Array.from([...SPKI_HEADER, ...point]));
const N = BigInt('0xffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551');

const device = p256.keygen();
const server = p256.keygen();
const ref = Uint8Array.from({ length: 16 }, (_, i) => i + 1);
const deviceId = Uint8Array.from([9, 8, 7, 6, 5, 4, 3, 2]);
const now = new Date('2027-02-13T21:00:00Z');

const pack: OfflinePack = {
  generatedAt: now.toISOString(),
  current: {
    key: 'dag-2027-02-13',
    eventId: null,
    carnivalDay: '2027-02-13',
    title: 'Carnaval · zaterdag 13 februari',
    startAt: now.toISOString(),
    endAt: null,
  },
  validFrom: '2027-02-13T18:00:00Z',
  validTo: '2027-02-14T05:00:00Z',
  serverKeys: [spki(p256.getPublicKey(server.secretKey, false))],
  tickets: [
    {
      ref: toBase64(ref),
      credentialVersion: 1,
      blocked: false,
      membershipActive: true,
      deviceShortId: toBase64(deviceId),
      devicePublicKey: spki(p256.getPublicKey(device.secretKey, false)),
      holderName: 'Piet van der Lid',
    },
  ],
};

function code(secretKey: Uint8Array, opts: { version?: number; at?: Date; cv?: number; highS?: boolean } = {}) {
  const unsigned = unsignedPayload({
    ref,
    credentialVersion: opts.cv ?? 1,
    deviceId,
    issuedAt: Math.floor((opts.at ?? now).getTime() / 1000),
    validFor: 45,
  });
  unsigned[0] = opts.version ?? 1;
  let signature = p256.sign(unsigned, secretKey);
  if (opts.highS) {
    // Toestellen en .NET normaliseren S niet: s' = n − s is net zo geldig.
    const s = BigInt('0x' + Array.from(signature.slice(32), (b) => b.toString(16).padStart(2, '0')).join(''));
    const high = (N - s).toString(16).padStart(64, '0');
    signature = Uint8Array.from([...signature.slice(0, 32), ...high.match(/../g)!.map((h) => parseInt(h, 16))]);
  }
  return base45(signedPayload(unsigned, signature));
}

describe('Offline scannen (fase 15, lichte variant)', () => {
  it('base45 heen en terug, ook met een spatie aan het begin', () => {
    const bytes = Uint8Array.from([1, 5, 200, 0, 255]);
    const text = base45(bytes);
    expect(text[0]).toBe(' ');
    expect(Array.from(base45Decode(text)!)).toEqual(Array.from(bytes));
    expect(base45Decode('ZZZ')).toBeNull();
  });

  it('geldige code van het toestel en van de server; tweede keer op dit toestel is groen-herhaald', () => {
    const seen = new Map<string, Date>();
    expect(checkOffline(pack, code(device.secretKey), now, seen)).toMatchObject({
      outcome: 'Admitted',
      holderName: 'Piet van der Lid',
    });
    expect(checkOffline(pack, code(device.secretKey, { highS: true }), now, seen).outcome).toBe('AdmittedAgain');
    expect(checkOffline(pack, code(server.secretKey, { version: 2 }), now, new Map()).outcome).toBe('Admitted');
  });

  it('nagemaakt, verlopen, oude versie, geblokkeerd en buiten het toegangsmoment is rood', () => {
    const other = p256.keygen();
    const check = (text: string, p: OfflinePack = pack, at = now) => checkOffline(p, text, at, new Map()).message;
    expect(check(code(other.secretKey))).toBe('Ongeldige handtekening: de code is nagemaakt of gewijzigd.');
    expect(check(code(device.secretKey, { at: new Date(now.getTime() - 3 * 60_000) }))).toMatch(/^Verlopen code/);
    expect(check(code(device.secretKey, { cv: 2 }))).toBe('Oude code: het ticket is opnieuw uitgegeven.');
    expect(check(code(device.secretKey), { ...pack, tickets: [{ ...pack.tickets[0]!, blocked: true }] })).toBe(
      'Ticket geblokkeerd.',
    );
    expect(
      check(code(device.secretKey, { at: new Date('2027-02-14T08:00:00Z') }), pack, new Date('2027-02-14T08:00:00Z')),
    ).toMatch(/geen toegangsmoment/);
    expect(check('GEEN CODE')).toBe('Geen geldige QR-code van De Vrolijke Drammers.');
  });

  it('wachtrij bewaart alleen code, tijd en uitkomst', async () => {
    await AsyncStorage.clear();
    expect(await enqueue(code(device.secretKey), 'Admitted', now)).toBe(1);
    expect(await queueLength()).toBe(1);
    const stored = JSON.parse((await AsyncStorage.getItem('dvd.offline-scans.v1'))!);
    expect(Object.keys(stored[0]).sort()).toEqual(['clientScanId', 'code', 'localOutcome', 'scannedAt']);
  });

  it('accepteert echte handtekeningen van de Secure Enclave (testvectoren uit de spike)', () => {
    const vectors: {
      publicKey: string;
      data: string;
      signature: string;
    }[] = require('../../../../tests/Drammers.UnitTests/DeviceKey/ios-security-framework.json');
    expect(vectors.length).toBeGreaterThan(2);
    for (const v of vectors) {
      expect(verifySignature(v.publicKey, fromBase64(v.data), fromBase64(v.signature))).toBe(true);
    }
    const changed = fromBase64(vectors[0]!.data);
    changed[changed.length - 1]! ^= 1;
    expect(verifySignature(vectors[0]!.publicKey, changed, fromBase64(vectors[0]!.signature))).toBe(false);
  });
});
