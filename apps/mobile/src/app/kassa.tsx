import { brand } from '@drammers/design-tokens';
import type { components } from '@drammers/api-client';
import { onlineManager } from '@tanstack/react-query';
import { CameraView, useCameraPermissions } from 'expo-camera';
import * as Haptics from 'expo-haptics';
import { router } from 'expo-router';
import { useRef, useState, useSyncExternalStore } from 'react';
import { ActivityIndicator, FlatList, Linking, Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { api } from '../api/client';
import { useKassaRecent, useMe } from '../api/queries';
import { AppText } from '../ui';

type KassaResult = components['schemas']['KassaResult'];
type KassaRecentRow = components['schemas']['KassaRecentRow'];

const useOnline = () => useSyncExternalStore(onlineManager.subscribe.bind(onlineManager), () => onlineManager.isOnline());

const GREEN = '#1E7A34';
const RED = '#C8101E';
/** Dubbelscan-waarschuwing (scherm B): oranje, geen harde blokkade. */
const ORANGE = '#B5720C';

/**
 * Kassa (fase 19c, rol Kassa, Figma "iOS / 9 Kassa – munten uitgeven"): de munten-QR van een lid scannen, de bestelling
 * zien en pas na het overhandigen "Bestelling uitgegeven" indrukken. Alleen online (besluit 30-09-2026); elke scan en
 * uitgifte komt in de Kassalog.
 */
export default function KassaScreen() {
  const insets = useSafeAreaInsets();
  const me = useMe();
  const online = useOnline();
  const canCollect = me.data?.permissions.includes('sale.collect') ?? false;
  const [permission, requestPermission] = useCameraPermissions();
  const [torch, setTorch] = useState(false);
  const [result, setResult] = useState<KassaResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [issuing, setIssuing] = useState(false);
  const [recentOpen, setRecentOpen] = useState(false);
  const recent = useKassaRecent(canCollect && recentOpen);
  const busy = useRef(false);
  const back = () => (router.canGoBack() ? router.back() : router.replace('/'));

  async function onScanned(code: string) {
    if (busy.current || result) return;
    busy.current = true;
    setError(null);
    try {
      const { data, error: problem, response } = await api.POST('/api/v1/kassa/scan', { body: { code } });
      if (data) {
        setResult(data);
        void Haptics.notificationAsync(
          data.outcome === 'Ready' ? Haptics.NotificationFeedbackType.Success : Haptics.NotificationFeedbackType.Error,
        ).catch(() => undefined);
      } else {
        setError((problem as { detail?: string } | undefined)?.detail ?? `Scannen lukt nu niet (${response.status}).`);
      }
    } catch {
      setError('Geen internet. Munten uitgeven kan alleen online.');
    } finally {
      busy.current = false;
    }
  }

  async function issue() {
    if (!result?.scanId) return;
    setIssuing(true);
    try {
      const { data, error: problem } = await api.POST('/api/v1/kassa/scans/{id}/issue', { params: { path: { id: result.scanId } } });
      if (data) {
        setResult(data);
        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success).catch(() => undefined);
      } else {
        setResult({
          ...result,
          scanId: null,
          outcome: 'Refused',
          title: 'Niet uitgeven',
          message: (problem as { detail?: string } | undefined)?.detail ?? 'Uitgeven lukt nu niet. Scan opnieuw.',
        });
      }
    } catch {
      setError('Geen internet. Probeer het opnieuw.');
    } finally {
      setIssuing(false);
    }
  }

  if (me.data && !canCollect) {
    return (
      <Message insets={insets.top} icon="🔒" title="Alleen voor de kassa" body="Munten uitgeven kan met de rol Kassa. Het bestuur kent die toe." onBack={back} />
    );
  }

  if (!permission || !me.data) {
    return (
      <View style={[styles.fill, styles.center, { backgroundColor: '#0B1620' }]}>
        <ActivityIndicator color="#FFFFFF" accessibilityLabel="Laden" />
      </View>
    );
  }

  if (recentOpen) {
    return <RecentList insets={insets} rows={recent.data ?? []} loading={recent.isLoading} onClose={() => setRecentOpen(false)} />;
  }

  if (result) {
    const ready = result.outcome === 'Ready';
    const done = result.outcome === 'Issued';
    // Scherm B (dubbelscan): een geldige scan waarvan dezelfde bestelling net ook op een andere kassa langskwam.
    const doubleScan = ready && !!result.recentOtherDeviceAt;
    // Scherm A (al uitgegeven): een weigering mét het eerdere uitgiftetijdstip.
    const alreadyIssued = !ready && !done && !!result.issuedAt;
    const background = doubleScan ? ORANGE : ready || done ? GREEN : RED;
    const title = doubleScan ? 'Let op' : result.title;
    const announce = doubleScan
      ? `Let op. ${result.holderName ?? ''}. Deze bestelling is net ook op een andere kassa gescand om ${result.recentOtherDeviceAt}. Controleer of dit dezelfde persoon is.`
      : `${result.title}. ${result.holderName ?? ''}. ${result.quantity ? `${result.quantity} munten. ` : ''}${result.message}`;
    return (
      <View style={[styles.fill, styles.result, { backgroundColor: background, paddingTop: insets.top + 16 }]} accessibilityViewIsModal>
        <View style={styles.top}>
          <View />
          <AppText variant="label" color="#FFFFFF">
            KASSA
          </AppText>
        </View>
        <View style={styles.resultBody} accessible accessibilityLiveRegion="assertive" accessibilityLabel={announce}>
          <View style={styles.circle}>
            <AppText variant="largeTitle" color="#FFFFFF" style={styles.icon}>
              {doubleScan ? '!' : ready || done ? '✓' : '✕'}
            </AppText>
          </View>
          <AppText variant="largeTitle" color="#FFFFFF" style={styles.centerText}>
            {title}
          </AppText>
          {result.holderName ? (
            <AppText variant="sectionHeader" color="#FFFFFF" style={styles.centerText}>
              {result.holderName}
            </AppText>
          ) : null}
          {doubleScan ? (
            <>
              <AppText variant="body" color="#FFFFFF" style={styles.centerText}>
                {result.quantity ? `${result.quantity} munten · ` : ''}bestelling {result.orderNumber}
              </AppText>
              <View style={styles.order}>
                <AppText variant="body" color="#FFFFFF" style={styles.centerText}>
                  Deze bestelling is net ook op een andere kassa gescand om {result.recentOtherDeviceAt}. Controleer of dit dezelfde persoon is.
                </AppText>
              </View>
            </>
          ) : alreadyIssued ? (
            <AppText variant="body" color="#FFFFFF" style={styles.centerText}>
              Afgehaald om {result.issuedAt}
              {result.orderNumber ? ` · bestelling ${result.orderNumber}` : ''}
            </AppText>
          ) : (
            <>
              {result.orderNumber ? (
                <AppText variant="body" color="#FFFFFF" style={styles.centerText}>
                  {result.paidWith ? `Betaald met ${result.paidWith} · ` : ''}bestelling {result.orderNumber}
                </AppText>
              ) : null}
              {result.quantity && (ready || done) ? (
                <View style={styles.order}>
                  <AppText variant="largeTitle" color="#FFFFFF" style={styles.centerText}>
                    {result.quantity} munten
                  </AppText>
                  <AppText variant="body" color="#FFFFFF" style={styles.centerText}>
                    {result.message}
                  </AppText>
                </View>
              ) : (
                <AppText variant="body" color="#FFFFFF" style={styles.centerText}>
                  {result.message}
                </AppText>
              )}
            </>
          )}
        </View>
        <View style={[styles.actions, { paddingBottom: insets.bottom + 24 }]}>
          {doubleScan ? (
            // De veilige keuze (annuleren) is groot en primair; "Toch uitgeven" klein eronder (geen harde blokkade).
            <>
              <Button label="Annuleren — niets uitgeven" color={ORANGE} onPress={() => setResult(null)} />
              <Pressable
                onPress={() => void issue()}
                disabled={issuing || !online}
                accessibilityRole="button"
                accessibilityState={{ disabled: issuing || !online }}
                hitSlop={8}
              >
                <AppText variant="bodyStrong" color="#FFFFFF" style={styles.centerText}>
                  {issuing ? 'Even geduld…' : 'Toch uitgeven'}
                </AppText>
              </Pressable>
            </>
          ) : ready ? (
            <>
              <AppText variant="caption" color="#FFFFFF" style={styles.centerText}>
                Tik pas op de knop als de munten zijn overhandigd. Dit wordt gelogd in het portal (Kassalog).
              </AppText>
              <Button label={issuing ? 'Even geduld…' : 'Bestelling uitgegeven'} color={GREEN} onPress={() => void issue()} disabled={issuing || !online} />
              <Pressable onPress={() => setResult(null)} accessibilityRole="button" hitSlop={8}>
                <AppText variant="bodyStrong" color="#FFFFFF" style={styles.centerText}>
                  Annuleren (niets uitgegeven)
                </AppText>
              </Pressable>
            </>
          ) : (
            <Button label="Volgende scannen" color={background} onPress={() => setResult(null)} />
          )}
        </View>
      </View>
    );
  }

  return (
    <View style={[styles.fill, { backgroundColor: '#0B1620', paddingTop: insets.top }]}>
      {permission.granted && online ? (
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
          <View style={styles.topRight}>
            <Pressable onPress={() => setRecentOpen(true)} accessibilityRole="button" accessibilityLabel="Recent uitgegeven" hitSlop={8}>
              <AppText variant="body" color="#FFFFFF">
                Recent
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
        </View>
        <AppText variant="largeTitle" color="#FFFFFF" accessibilityRole="header">
          Kassa
        </AppText>
        <View style={styles.finderWrap}>
          <View style={styles.finder} />
          {!online ? (
            <AppText variant="bodyStrong" color="#FFB4B4" style={styles.centerText} accessibilityRole="alert">
              Geen internet. Munten uitgeven kan alleen online.
            </AppText>
          ) : permission.granted ? (
            <AppText variant="body" color="rgba(255,255,255,0.85)" style={styles.centerText}>
              Richt de camera op de munten-QR in de app van het lid
            </AppText>
          ) : (
            <View style={styles.permission}>
              <AppText variant="body" color="#FFFFFF" style={styles.centerText}>
                Geef toegang tot de camera om munten-QR&apos;s te scannen.
              </AppText>
              <Button
                label={permission.canAskAgain ? 'Camera toestaan' : 'Open instellingen'}
                color={brand.navy}
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
      </View>
    </View>
  );
}

/** Witte knop met gekleurde tekst, zoals in Figma. */
function Button({ label, color, onPress, disabled }: { label: string; color: string; onPress: () => void; disabled?: boolean }) {
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      style={({ pressed }) => [styles.button, (pressed || disabled) && styles.pressed]}
    >
      <AppText variant="bodyStrong" color={color}>
        {label}
      </AppText>
    </Pressable>
  );
}

function Message({ insets, icon, title, body, onBack }: { insets: number; icon: string; title: string; body: string; onBack: () => void }) {
  return (
    <View style={[styles.fill, styles.result, { backgroundColor: brand.navy, paddingTop: insets + 24 }]}>
      <View style={styles.resultBody}>
        <View style={styles.circle}>
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
      <View style={styles.actions}>
        <Button label="Terug" color={brand.navy} onPress={onBack} />
      </View>
    </View>
  );
}

/**
 * "Recent" (fase 19c, scherm C): alle uitgiftes van vanavond op dit toestel (dag-grens 06:00, dus 's ochtends weer
 * leeg). Donkere stijl zoals de rest van het kassascherm; per regel de lidnaam, het aantal munten, de tijd en de
 * betaalwijze met een groen vinkje.
 */
function RecentList({ insets, rows, loading, onClose }: { insets: { top: number; bottom: number }; rows: KassaRecentRow[]; loading: boolean; onClose: () => void }) {
  return (
    <View style={[styles.fill, { backgroundColor: '#0B1620', paddingTop: insets.top }]}>
      <View style={[styles.top, styles.recentHeader]}>
        <Pressable onPress={onClose} accessibilityRole="button" accessibilityLabel="Terug naar scannen" hitSlop={8}>
          <AppText variant="body" color="#FFFFFF">
            ‹ Scannen
          </AppText>
        </Pressable>
        <AppText variant="label" color="#FFFFFF">
          RECENT
        </AppText>
      </View>
      <AppText variant="largeTitle" color="#FFFFFF" accessibilityRole="header" style={styles.recentTitle}>
        Vanavond uitgegeven
      </AppText>
      {loading ? (
        <View style={[styles.fill, styles.center]}>
          <ActivityIndicator color="#FFFFFF" accessibilityLabel="Laden" />
        </View>
      ) : rows.length === 0 ? (
        <View style={[styles.fill, styles.center, styles.recentEmpty]}>
          <AppText variant="body" color="rgba(255,255,255,0.7)" style={styles.centerText}>
            Nog niets uitgegeven op dit toestel vanavond.
          </AppText>
        </View>
      ) : (
        <FlatList
          data={rows}
          keyExtractor={(_, index) => String(index)}
          contentContainerStyle={{ paddingHorizontal: 24, paddingBottom: insets.bottom + 24, gap: 12 }}
          renderItem={({ item }) => (
            <View style={styles.recentRow}>
              <View style={styles.recentCheck}>
                <AppText variant="bodyStrong" color="#FFFFFF">
                  ✓
                </AppText>
              </View>
              <View style={styles.recentRowBody}>
                <AppText variant="bodyStrong" color="#FFFFFF">
                  {item.holderName ?? 'Onbekend lid'}
                </AppText>
                <AppText variant="caption" color="rgba(255,255,255,0.7)">
                  {item.quantity ?? 0} munten · {item.time}
                  {item.paidWith ? ` · ${item.paidWith}` : ''}
                </AppText>
              </View>
            </View>
          )}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  center: { alignItems: 'center', justifyContent: 'center' },
  overlay: { flex: 1, paddingHorizontal: 24, gap: 12 },
  top: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 8 },
  topRight: { flexDirection: 'row', alignItems: 'center', gap: 20 },
  recentHeader: { paddingHorizontal: 24 },
  recentTitle: { paddingHorizontal: 24, paddingVertical: 12 },
  recentEmpty: { paddingHorizontal: 24 },
  recentRow: { flexDirection: 'row', alignItems: 'center', gap: 12, borderRadius: 16, padding: 16, backgroundColor: 'rgba(255,255,255,0.08)' },
  recentCheck: { width: 32, height: 32, borderRadius: 16, alignItems: 'center', justifyContent: 'center', backgroundColor: GREEN },
  recentRowBody: { flex: 1, gap: 2 },
  finderWrap: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 16 },
  finder: { width: 260, height: 260, borderRadius: 24, borderWidth: 4, borderColor: '#FFFFFF' },
  permission: { gap: 12, alignSelf: 'stretch' },
  result: { paddingHorizontal: 24, justifyContent: 'space-between' },
  resultBody: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 12 },
  circle: {
    width: 120,
    height: 120,
    borderRadius: 60,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: 'rgba(255,255,255,0.18)',
  },
  icon: { fontSize: 64, lineHeight: 72 },
  order: { alignSelf: 'stretch', borderRadius: 16, padding: 16, gap: 4, backgroundColor: 'rgba(255,255,255,0.18)' },
  centerText: { textAlign: 'center' },
  actions: { gap: 12 },
  button: { minHeight: 56, borderRadius: 12, alignItems: 'center', justifyContent: 'center', backgroundColor: '#FFFFFF' },
  pressed: { opacity: 0.7 },
});
