import * as Crypto from 'expo-crypto';
import { useState } from 'react';
import { Share, StyleSheet, View } from 'react-native';
import { DeviceKey, type GeneratedDeviceKey } from '../../../modules/device-key';
import {
  base45,
  fromBase64,
  qrVersionForAlphanumeric,
  signedPayload,
  toBase64,
  unsignedPayload,
} from '../../features/deviceQr';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, LargeTitleHeader, Screen } from '../../ui';

const ALIAS = 'spike';
const RUNS = 20;

interface SignResult {
  payload: string;
  signature: string;
  qrText: string;
  averageMs: number;
  maxMs: number;
}

/**
 * Spike OQ-68 (ADR-005, fase 9c): maakt een hardwaresleutel, ondertekent de QR-payload en meet de tijd. Alleen in
 * ontwikkeling of in de EAS-build met `EXPO_PUBLIC_DEVICE_KEY_SPIKE=1`; geen productfunctie.
 */
export default function SleutelTestScreen() {
  const { colors } = useTheme();
  const [key, setKey] = useState<(GeneratedDeviceKey & { generateMs: number }) | null>(null);
  const [result, setResult] = useState<SignResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const info = DeviceKey?.getInfo();

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await action();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  const generate = () =>
    run(async () => {
      const started = Date.now();
      // Met een challenge levert Android de key-attestation-keten mee (voor OQ-73).
      const generated = await DeviceKey!.generateKeyAsync(ALIAS, toBase64(Crypto.getRandomBytes(16)));
      setKey({ ...generated, generateMs: Date.now() - started });
      setResult(null);
    });

  const sign = () =>
    run(async () => {
      const unsigned = unsignedPayload({
        ref: Crypto.getRandomBytes(16),
        credentialVersion: 1,
        deviceId: Crypto.getRandomBytes(8),
        issuedAt: Math.floor(Date.now() / 1000),
        validFor: 45,
      });
      const data = toBase64(unsigned);
      const timings: number[] = [];
      let signature = '';
      for (let i = 0; i < RUNS; i++) {
        const started = Date.now();
        signature = await DeviceKey!.signAsync(ALIAS, data);
        timings.push(Date.now() - started);
      }
      const signed = signedPayload(unsigned, fromBase64(signature));
      setResult({
        payload: data,
        signature,
        qrText: base45(signed),
        averageMs: Math.round(timings.reduce((a, b) => a + b, 0) / RUNS),
        maxMs: Math.max(...timings),
      });
    });

  const remove = () =>
    run(async () => {
      await DeviceKey!.deleteKeyAsync(ALIAS);
      setKey(null);
      setResult(null);
    });

  const share = () =>
    Share.share({
      message: JSON.stringify(
        { platform: info?.platform, securityLevel: key?.securityLevel, publicKey: key?.publicKey, ...result },
        null,
        2,
      ),
    });

  return (
    <Screen>
      <BackLink label="Meer" />
      <LargeTitleHeader title="Hardwaresleutel" />
      <View style={styles.content}>
        {!DeviceKey ? (
          <Card style={styles.card}>
            <AppText variant="body">
              De module zit niet in deze app. Expo Go kan geen eigen native code laden; gebruik een development build of
              de EAS-build ‘spike’ (zie docs/runbooks/hardwaresleutel-spike.md).
            </AppText>
          </Card>
        ) : (
          <>
            <Card style={styles.card}>
              <Row label="Platform" value={info?.platform ?? '?'} />
              <Row
                label={info?.platform === 'ios' ? 'Secure Enclave' : 'StrongBox'}
                value={info?.secureHardware ? 'aanwezig' : 'niet aanwezig'}
              />
              {key ? (
                <>
                  <Row label="Sleutel in" value={key.securityLevel} />
                  <Row label="Aanmaken" value={`${key.generateMs} ms`} />
                  <Row
                    label="Attestatieketen"
                    value={key.attestation.length ? `${key.attestation.length} certificaten` : 'geen'}
                  />
                </>
              ) : null}
              {result ? (
                <>
                  <Row
                    label={`Ondertekenen (${RUNS}×)`}
                    value={`gem. ${result.averageMs} ms, max. ${result.maxMs} ms`}
                  />
                  <Row
                    label="QR-tekst"
                    value={`${result.qrText.length} tekens, versie ${qrVersionForAlphanumeric(result.qrText.length)}`}
                  />
                </>
              ) : null}
            </Card>
            {error ? (
              <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
                {error}
              </AppText>
            ) : null}
            <Button label={key ? 'Nieuwe sleutel maken' : 'Sleutel maken'} onPress={generate} disabled={busy} />
            <Button label="QR-code ondertekenen" variant="secondary" onPress={sign} disabled={busy || !key} />
            <Button label="Resultaat delen" variant="secondary" onPress={share} disabled={!result} />
            <Button label="Sleutel verwijderen" variant="secondary" onPress={remove} disabled={busy || !key} />
          </>
        )}
      </View>
    </Screen>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.row}>
      <AppText variant="body" color={colors.textSecondary}>
        {label}
      </AppText>
      <AppText variant="bodyStrong">{value}</AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' },
  content: { paddingHorizontal: 20, gap: 16 },
  card: { padding: 16, gap: 10 },
});
