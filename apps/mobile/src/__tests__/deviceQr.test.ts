import {
  base45,
  fromBase64,
  qrVersionForAlphanumeric,
  signedPayload,
  toBase64,
  unsignedPayload,
} from '../features/deviceQr';

describe('QR-payload (ADR-005, spike OQ-68)', () => {
  const fields = {
    ref: Uint8Array.from({ length: 16 }, (_, i) => i),
    credentialVersion: 1,
    deviceId: Uint8Array.from({ length: 8 }, (_, i) => 0xa0 + i),
    issuedAt: 1_800_000_000,
    validFor: 45,
  };

  it('legt de velden op vaste posities, big-endian', () => {
    const bytes = unsignedPayload(fields);
    expect(bytes.length).toBe(33);
    expect(bytes[0]).toBe(1);
    expect(Array.from(bytes.slice(1, 17))).toEqual(Array.from(fields.ref));
    expect(Array.from(bytes.slice(17, 19))).toEqual([0, 1]);
    expect(Array.from(bytes.slice(19, 27))).toEqual(Array.from(fields.deviceId));
    expect(new DataView(bytes.buffer).getUint32(27)).toBe(1_800_000_000);
    expect(Array.from(bytes.slice(31, 33))).toEqual([0, 45]);
  });

  it('is ondertekend 97 bytes en past base45-gecodeerd in QR-versie 6', () => {
    const signed = signedPayload(unsignedPayload(fields), new Uint8Array(64).fill(0xff));
    const text = base45(signed);
    expect(signed.length).toBe(97);
    expect(text.length).toBe(146);
    expect(qrVersionForAlphanumeric(text.length)).toBe(6);
  });

  it('weigert verkeerde lengtes', () => {
    expect(() => unsignedPayload({ ...fields, ref: new Uint8Array(15) })).toThrow();
    expect(() => signedPayload(unsignedPayload(fields), new Uint8Array(70))).toThrow();
  });

  it('base45 volgens de voorbeelden uit RFC 9285', () => {
    const utf8 = (s: string) => Uint8Array.from(s, (c) => c.charCodeAt(0));
    expect(base45(utf8('AB'))).toBe('BB8');
    expect(base45(utf8('Hello!!'))).toBe('%69 VD92EX0');
    expect(base45(utf8('base-45'))).toBe('UJCLQE7W581');
    expect(base45(utf8('ietf!'))).toBe('QED8WEX0');
  });

  it('base64 heen en terug', () => {
    const bytes = Uint8Array.from([0, 1, 254, 255, 128]);
    expect(Array.from(fromBase64(toBase64(bytes)))).toEqual(Array.from(bytes));
  });
});
