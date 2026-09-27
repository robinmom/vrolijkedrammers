import { requireOptionalNativeModule } from 'expo';

/** Waar de private sleutel staat; alleen SecureEnclave en StrongBox/TrustedEnvironment zijn hardwaregebonden. */
export type SecurityLevel = 'SecureEnclave' | 'StrongBox' | 'TrustedEnvironment' | 'UnknownSecure' | 'Software';

export interface DeviceKeyInfo {
  platform: 'ios' | 'android';
  /** Secure Enclave (iOS) of StrongBox (Android) aanwezig. */
  secureHardware: boolean;
}

export interface GeneratedDeviceKey {
  /** SubjectPublicKeyInfo (DER), base64. */
  publicKey: string;
  securityLevel: SecurityLevel;
  /** Android: key-attestation-keten (X.509 DER, base64), alleen met een challenge. iOS: leeg (App Attest is apart). */
  attestation: string[];
}

interface DeviceKeyNativeModule {
  getInfo(): DeviceKeyInfo;
  generateKeyAsync(alias: string, challenge?: string | null): Promise<GeneratedDeviceKey>;
  getPublicKeyAsync(alias: string): Promise<string | null>;
  /** Ondertekent base64-data met ECDSA P-256/SHA-256; geeft r‖s (64 bytes) als base64. */
  signAsync(alias: string, data: string): Promise<string>;
  deleteKeyAsync(alias: string): Promise<void>;
}

/**
 * Spike OQ-68 (ADR-005): hardwaresleutel via een eigen Expo-module. `null` in Expo Go en op het web: daar zit de
 * native code niet in (alleen in een development build of een EAS-build).
 */
export const DeviceKey = requireOptionalNativeModule<DeviceKeyNativeModule>('DeviceKey');
