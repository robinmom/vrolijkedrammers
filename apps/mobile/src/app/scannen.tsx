import { brand } from '@drammers/design-tokens';
import type { components } from '@drammers/api-client';
import { useQueryClient } from '@tanstack/react-query';
import { CameraView, useCameraPermissions } from 'expo-camera';
import * as Haptics from 'expo-haptics';
import { router } from 'expo-router';
import { useRef, useState } from 'react';
import { ActivityIndicator, Linking, Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { api } from '../api/client';
import { queryKeys, useAccessStatus, useMe } from '../api/queries';
import { AppText } from '../ui';

type AccessResult = components['schemas']['AccessResult'];
type AccessCounts = components['schemas']['AccessCounts'];

const dayTime = new Intl.DateTimeFormat('nl-NL', {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  hour: '2-digit',
  minute: '2-digit',
  timeZone: 'Europe/Amsterdam',
});
const time = new Intl.DateTimeFormat('nl-NL', { hour: '2-digit', minute: '2-digit', timeZone: 'Europe/Amsterdam' });

/** Resultaat: altijd kleur + icoon + tekst (en trillen), dus nooit alleen kleur (Figma 📷 Toegangscontrole). */
const looks: Record<AccessResult['outcome'], { background: string; foreground: string; icon: string }> = {
  Admitted: { background: '#1E7A34', foreground: '#FFFFFF', icon: '✓' },
  AdmittedAgain: { background: '#1E7A34', foreground: '#FFFFFF', icon: '✓' },
  Warning: { background: brand.yellow, foreground: brand.navy, icon: '!' },
  Refused: { background: '#C8101E', foreground: '#FFFFFF', icon: '✕' },
};

const haptic: Record<AccessResult['outcome'], Haptics.NotificationFeedbackType> = {
  Admitted: Haptics.NotificationFeedbackType.Success,
  AdmittedAgain: Haptics.NotificationFeedbackType.Success,
  Warning: Haptics.NotificationFeedbackType.Warning,
  Refused: Haptics.NotificationFeedbackType.Error,
};

/**
 * Scannen bij de deur (fase 14b, rol Deurcontrole): camera op de QR-code in Mijn QR, daarna groen/oranje/rood met
 * de naam en de reden. Bij oranje beslist het deurpersoneel ("Toch toelaten" of "Weigeren"). Alleen tijdens een
 * activiteit met toegangscontrole; de teller toont binnen / scans / geweigerd.
 */
export default function ScannenScreen() {
  const insets = useSafeAreaInsets();
  const me = useMe();
  const canScan = me.data?.permissions.includes('ticket.scan') ?? false;
  const status = useAccessStatus(canScan);
  const queryClient = useQueryClient();
  const [permission, requestPermission] = useCameraPermissions();
  const [torch, setTorch] = useState(false);
  const [result, setResult] = useState<AccessResult | null>(null);
  const [counts, setCounts] = useState<AccessCounts | null>(null);
  const [error, setError] = useState<string | null>(null);
  const busy = useRef(false);

  const current = status.data?.current;
  const shownCounts = counts ?? status.data?.counts ?? null;

  async function onScanned(code: string) {
    if (busy.current || result) return;
    busy.current = true;
    setError(null);
    try {
      const { data, error: problem, response } = await api.POST('/api/v1/access/scan', { body: { code } });
      if (data) {
        setResult(data);
        setCounts(data.counts);
        void Haptics.notificationAsync(haptic[data.outcome]).catch(() => undefined);
      } else {
        setError((problem as { detail?: string } | undefined)?.detail ?? `Scannen lukt nu niet (${response.status}).`);
        if (response.status === 409) void queryClient.invalidateQueries({ queryKey: queryKeys.accessStatus });
      }
    } catch {
      setError('Geen verbinding. Scannen werkt alleen met internet.');
    } finally {
      busy.current = false;
    }
  }

  async function decide(admit: boolean) {
    if (!result?.scanId) return;
    const { data } = await api
      .POST('/api/v1/access/scans/{id}/decision', { params: { path: { id: result.scanId } }, body: { admit } })
      .catch(() => ({ data: undefined }));
    if (data) setCounts(data);
    setResult(null);
  }

  const back = () => (router.canGoBack() ? router.back() : router.replace('/'));

  if (me.data && !canScan) {
    return (
      <Message
        insets={insets.top}
        icon="🔒"
        title="Alleen voor deurcontrole"
        body="Scannen kan met de rol Deurcontrole. Het bestuur kent die toe."
        action={{ label: 'Terug', onPress: back }}
      />
    );
  }

  if (status.isPending || !permission) {
    return (
      <View style={[styles.fill, styles.center, { backgroundColor: '#0B1620' }]}>
        <ActivityIndicator color="#FFFFFF" accessibilityLabel="Laden" />
      </View>
    );
  }

  if (!current) {
    const next = status.data?.next;
    return (
      <Message
        insets={insets.top}
        icon="⏸"
        title="Geen toegangscontrole nu"
        body={`Er is op dit moment geen activiteit met toegangscontrole.${next ? `\n\nVolgende: ${dayTime.format(new Date(next.startAt))} – ${next.title}` : ''}`}
        action={{ label: 'Terug', onPress: back }}
      />
    );
  }

  if (result) {
    const look = looks[result.outcome];
    return (
      <View
        style={[styles.fill, styles.result, { backgroundColor: look.background, paddingTop: insets.top + 24 }]}
        accessibilityViewIsModal
      >
        <View
          style={styles.resultBody}
          accessible
          accessibilityLiveRegion="assertive"
          accessibilityLabel={`${result.title}. ${result.holderName ?? ''}. ${result.message}`}
        >
          <View style={[styles.circle, { backgroundColor: `${look.foreground}2E` }]}>
            <AppText variant="largeTitle" color={look.foreground} style={styles.icon}>
              {look.icon}
            </AppText>
          </View>
          <AppText variant="largeTitle" color={look.foreground} style={styles.centerText}>
            {result.title}
          </AppText>
          {result.holderName ? (
            <AppText variant="sectionHeader" color={look.foreground} style={styles.centerText}>
              {result.holderName}
            </AppText>
          ) : null}
          <AppText variant="body" color={look.foreground} style={styles.centerText}>
            {result.message}
          </AppText>
        </View>
        <View style={[styles.actions, { paddingBottom: insets.bottom + 24 }]}>
          {result.needsDecision ? (
            <>
              <ResultButton
                label="Toch toelaten"
                solid
                color={brand.navy}
                textColor="#FFFFFF"
                onPress={() => void decide(true)}
              />
              <ResultButton
                label="Weigeren"
                color={brand.navy}
                textColor={brand.navy}
                onPress={() => void decide(false)}
              />
            </>
          ) : (
            <ResultButton
              label="Volgende scannen"
              solid
              color="#FFFFFF"
              textColor={look.background}
              onPress={() => setResult(null)}
            />
          )}
        </View>
      </View>
    );
  }

  return (
    <View style={[styles.fill, { backgroundColor: '#0B1620', paddingTop: insets.top }]}>
      {permission.granted ? (
        <CameraView
          style={StyleSheet.absoluteFill}
          facing="back"
          enableTorch={torch}
          barcodeScannerSettings={{ barcodeTypes: ['qr'] }}
          onBarcodeScanned={({ data }) => void onScanned(data)}
        />
      ) : null}
      <View style={styles.overlay} pointerEvents="box-none">
        <View style={styles.top}>
          <Pressable onPress={back} accessibilityRole="button" accessibilityLabel="Terug" hitSlop={8}>
            <AppText variant="body" color="#FFFFFF">
              ‹ Terug
            </AppText>
          </Pressable>
          <Pressable
            onPress={() => setTorch((t) => !t)}
            accessibilityRole="switch"
            accessibilityState={{ checked: torch }}
            accessibilityLabel="Zaklamp"
            hitSlop={8}
          >
            <AppText variant="body" color="#FFFFFF">
              {torch ? '🔦 aan' : '🔦'}
            </AppText>
          </Pressable>
        </View>
        <AppText variant="largeTitle" color="#FFFFFF" accessibilityRole="header">
          Scannen
        </AppText>
        <View style={styles.pill}>
          <View style={styles.dot} />
          <AppText variant="caption" color="#FFFFFF">
            {current.title} · {time.format(new Date(current.startAt))}
            {current.endAt ? `–${time.format(new Date(current.endAt))}` : ''}
          </AppText>
        </View>
        <View style={styles.finderWrap}>
          <View style={styles.finder} />
          {permission.granted ? (
            <AppText variant="body" color="rgba(255,255,255,0.85)" style={styles.centerText}>
              Richt de camera op de QR-code in Mijn QR
            </AppText>
          ) : (
            <View style={styles.permission}>
              <AppText variant="body" color="#FFFFFF" style={styles.centerText}>
                Geef toegang tot de camera om QR-codes te scannen.
              </AppText>
              <ResultButton
                label={permission.canAskAgain ? 'Camera toestaan' : 'Open instellingen'}
                solid
                color="#FFFFFF"
                textColor={brand.navy}
                onPress={() => (permission.canAskAgain ? void requestPermission() : void Linking.openSettings())}
              />
            </View>
          )}
          {error ? (
            <AppText variant="bodyStrong" color="#FFB4B4" style={styles.centerText} accessibilityRole="alert">
              {error}
            </AppText>
          ) : null}
        </View>
        <View
          style={[styles.stats, { marginBottom: insets.bottom + 16 }]}
          accessible
          accessibilityLabel={`${shownCounts?.inside ?? 0} binnen, ${shownCounts?.scans ?? 0} scans, ${shownCounts?.refused ?? 0} geweigerd`}
        >
          {(
            [
              [shownCounts?.inside ?? 0, 'binnen'],
              [shownCounts?.scans ?? 0, 'scans'],
              [shownCounts?.refused ?? 0, 'geweigerd'],
            ] as const
          ).map(([value, label]) => (
            <View key={label} style={styles.stat}>
              <AppText variant="sectionHeader" color="#FFFFFF">
                {value}
              </AppText>
              <AppText variant="caption" color="rgba(255,255,255,0.75)">
                {label}
              </AppText>
            </View>
          ))}
        </View>
      </View>
    </View>
  );
}

function ResultButton({
  label,
  onPress,
  solid,
  color,
  textColor,
}: {
  label: string;
  onPress: () => void;
  solid?: boolean;
  color: string;
  textColor: string;
}) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      style={({ pressed }) => [
        styles.button,
        solid ? { backgroundColor: color } : { borderWidth: 2, borderColor: color },
        pressed && styles.pressed,
      ]}
    >
      <AppText variant="bodyStrong" color={textColor}>
        {label}
      </AppText>
    </Pressable>
  );
}

function Message({
  insets,
  icon,
  title,
  body,
  action,
}: {
  insets: number;
  icon: string;
  title: string;
  body: string;
  action: { label: string; onPress: () => void };
}) {
  return (
    <View style={[styles.fill, styles.result, { backgroundColor: brand.navy, paddingTop: insets + 24 }]}>
      <View style={styles.resultBody}>
        <View style={[styles.circle, { backgroundColor: 'rgba(255,255,255,0.18)' }]}>
          <AppText variant="largeTitle" color="#FFFFFF" style={styles.icon}>
            {icon}
          </AppText>
        </View>
        <AppText variant="largeTitle" color="#FFFFFF" style={styles.centerText} accessibilityRole="header">
          {title}
        </AppText>
        <AppText variant="body" color="#FFFFFF" style={styles.centerText}>
          {body}
        </AppText>
      </View>
      <View style={[styles.actions, { paddingBottom: 40 }]}>
        <ResultButton label={action.label} color="#FFFFFF" textColor="#FFFFFF" onPress={action.onPress} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  center: { alignItems: 'center', justifyContent: 'center' },
  overlay: { flex: 1, paddingHorizontal: 24, gap: 12 },
  top: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 8 },
  pill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    alignSelf: 'flex-start',
    backgroundColor: 'rgba(255,255,255,0.14)',
    borderRadius: 999,
    paddingHorizontal: 12,
    paddingVertical: 6,
  },
  dot: { width: 8, height: 8, borderRadius: 4, backgroundColor: brand.green },
  finderWrap: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 16 },
  finder: { width: 260, height: 260, borderRadius: 24, borderWidth: 4, borderColor: '#FFFFFF' },
  permission: { gap: 12, alignSelf: 'stretch' },
  stats: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    backgroundColor: 'rgba(11,22,32,0.7)',
    borderRadius: 16,
    paddingHorizontal: 20,
    paddingVertical: 14,
  },
  stat: { alignItems: 'center' },
  result: { paddingHorizontal: 24, justifyContent: 'space-between' },
  resultBody: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 12 },
  circle: { width: 120, height: 120, borderRadius: 60, alignItems: 'center', justifyContent: 'center' },
  icon: { fontSize: 64, lineHeight: 72 },
  centerText: { textAlign: 'center' },
  actions: { gap: 12 },
  button: { minHeight: 56, borderRadius: 12, alignItems: 'center', justifyContent: 'center' },
  pressed: { opacity: 0.8 },
});
