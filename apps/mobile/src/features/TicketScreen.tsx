import { brand } from '@drammers/design-tokens';
import { onlineManager, useQueryClient } from '@tanstack/react-query';
import * as Brightness from 'expo-brightness';
import { createEventInCalendarAsync } from 'expo-calendar/legacy';
import { router, useFocusEffect } from 'expo-router';
import { usePreventScreenCapture, useScreenshotListener } from 'expo-screen-capture';
import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { ActivityIndicator, Alert, Platform, StyleSheet, View } from 'react-native';
import QRCode from 'react-native-qrcode-svg';
import { queryKeys, useChildTicket, useMyTicket } from '../api/queries';
import { useSessionStatus } from '../auth/useSession';
import {
  bindThisDevice,
  deviceCode,
  forgetTicket,
  hasHardwareKey,
  loadTicket,
  REFRESH_SECONDS,
  saveTicket,
  serverCode,
  syncDeviceKey,
  type CachedTicket,
  type MyTicket,
  validityText,
} from './ticket';
import { useTheme } from '../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, ErrorState, LargeTitleHeader, Screen } from '../ui';

const useOnline = () =>
  useSyncExternalStore(onlineManager.subscribe.bind(onlineManager), () => onlineManager.isOnline());

/**
 * Mijn QR (fase 13b, Figma 🎟️ Mijn QR, ADR-005). De code ververst elke 30 seconden. Met een hardwaresleutel maakt het
 * toestel de code zelf (werkt ook zonder internet); zonder (Expo Go, oudere toestellen) haalt de app een door de server
 * ondertekende code op. Zolang het scherm open is: maximale helderheid en geen schermafdrukken (Android), met een
 * melding bij een schermafdruk op iOS.
 */
export function TicketScreen({ childId, childName }: { childId?: string; childName?: string }) {
  const { colors } = useTheme();
  const status = useSessionStatus();
  const own = useMyTicket(!childId);
  const child = useChildTicket(childId ?? null);
  const ticket = childId ? child : own;
  // Fase 17: de QR van een kind op de telefoon van de ouder; teksten over "je ticket" gaan dan over het kind.
  const whose = childId ? `Het ticket van ${childName ?? 'je kind'}` : 'Je ticket';
  const queryClient = useQueryClient();
  const [cached, setCached] = useState<CachedTicket | null>(null);
  const [binding, setBinding] = useState(false);
  const [bindError, setBindError] = useState<string | null>(null);
  const autoBound = useRef(false);

  usePreventScreenCapture();
  useScreenshotListener(() => {
    if (Platform.OS === 'ios') {
      Alert.alert(
        'Schermafdruk werkt niet',
        'Bij de ingang werkt alleen de live code: die verandert elke 30 seconden.',
      );
    }
  });
  useFocusEffect(
    useCallback(() => {
      Brightness.setBrightnessAsync(1).catch(() => undefined);
      return () => {
        Brightness.restoreSystemBrightnessAsync().catch(() => undefined);
      };
    }, []),
  );

  useEffect(() => {
    loadTicket(childId).then(setCached, () => undefined);
  }, [childId]);

  useEffect(() => {
    if (status === 'signedOut') {
      void forgetTicket();
    }
  }, [status]);

  const refresh = useCallback(
    () => queryClient.invalidateQueries({ queryKey: childId ? queryKeys.childTicket(childId) : queryKeys.myTicket }),
    [queryClient, childId],
  );

  const bind = useCallback(async () => {
    setBinding(true);
    setBindError(null);
    const result = await bindThisDevice(childId).catch(() => ({
      ok: false as const,
      message: 'Geen verbinding. Probeer het opnieuw.',
    }));
    if (!result.ok) setBindError(result.message);
    await refresh();
    setBinding(false);
  }, [refresh, childId]);

  // Ticket zonder toestel: automatisch aan dit toestel koppelen. Kent de API de sleutel niet meer: opnieuw registreren.
  useEffect(() => {
    const data = ticket.data;
    if (!data || !(data.state === 'Valid' || data.state === 'NotYetValid')) return;
    void saveTicket(data, childId).then(() => loadTicket(childId).then(setCached));
    if (!data.boundDeviceName && !autoBound.current) {
      autoBound.current = true;
      void bind();
    } else if (data.boundToThisDevice && !data.deviceHasHardwareKey && hasHardwareKey()) {
      void syncDeviceKey(false).then((known) => (known ? refresh() : undefined));
    }
  }, [ticket.data, bind, refresh, childId]);

  const header = (
    <>
      <BackLink label={childId ? (childName ?? 'Terug') : 'Home'} />
      <LargeTitleHeader title={childId ? `QR van ${childName ?? 'je kind'}` : 'Mijn QR'} />
    </>
  );

  if (status === 'signedOut') {
    return (
      <Screen>
        {header}
        <View style={styles.content}>
          <Notice
            icon="🔑"
            tint={brand.blue}
            title="Mijn QR is voor leden"
            body="Log in met je ledenaccount; daarna staat je ledenticket voor carnaval hier."
            action={{ label: 'Inloggen', onPress: () => router.push('/meer/inloggen') }}
          />
        </View>
      </Screen>
    );
  }

  const data = ticket.data;
  // Zonder internet: met een hardwaresleutel werkt de code met de gegevens op het toestel.
  if (!data && ticket.isError && cached && hasHardwareKey()) {
    return (
      <Screen>
        {header}
        <View style={styles.content}>
          <LiveTicket holder={cached} source="device" cached={cached} childId={childId} />
        </View>
      </Screen>
    );
  }

  if (!data) {
    return (
      <Screen>
        {header}
        {ticket.isError ? (
          <ErrorState
            message={`${whose} ophalen lukt nu niet. Controleer je verbinding en probeer het opnieuw.`}
            action={{ label: 'Opnieuw', onPress: () => void refresh() }}
          />
        ) : (
          <ActivityIndicator style={styles.loading} accessibilityLabel="Laden" />
        )}
      </Screen>
    );
  }

  return (
    <Screen>
      {header}
      <View style={styles.content}>
        {data.state === 'None' || data.state === 'Blocked' ? (
          <TicketCard ticket={data} muted>
            <Notice
              icon="!"
              tint={brand.red}
              title="Geen geldig ticket"
              body={data.message}
              action={{ label: 'Contact opnemen', onPress: () => router.push('/meer/contact'), secondary: true }}
            />
          </TicketCard>
        ) : data.state === 'Ended' ? (
          <TicketCard ticket={data} muted>
            <Notice icon="🎉" tint={brand.yellow} title="Carnaval is voorbij" body={data.message} />
          </TicketCard>
        ) : data.state === 'NotYetValid' ? (
          <>
            <TicketCard ticket={data}>
              <Notice
                icon="📅"
                tint={brand.yellow}
                title="Je QR verschijnt bij carnaval"
                body={`Je ledenticket is klaar. De code is geldig van ${validityText(data.validFrom, data.validTo)} en verschijnt dan hier.`}
                action={{
                  label: 'Zet in mijn agenda',
                  secondary: true,
                  onPress: () =>
                    void createEventInCalendarAsync({
                      title: `Carnaval ${data.carnivalYearName ?? ''} – De Vrolijke Drammers`.trim(),
                      startDate: new Date(data.validFrom!),
                      endDate: new Date(data.validTo!),
                      allDay: true,
                      timeZone: 'Europe/Amsterdam',
                    }).catch(() => undefined),
                }}
              />
            </TicketCard>
            <InfoRows
              rows={[
                data.boundToThisDevice ? 'Gekoppeld aan dit toestel' : 'Wordt gekoppeld aan dit toestel',
                'Actief lid volgens de ledenadministratie',
                data.deviceHasHardwareKey
                  ? 'Werkt straks ook zonder internet'
                  : 'Straks is internet nodig voor de code',
              ]}
            />
            <AppText variant="caption" color={colors.textSecondary}>
              Tip: open Mijn QR één keer vóór carnaval met internet; daarna werkt het ook offline.
            </AppText>
          </>
        ) : data.boundToThisDevice ? (
          <LiveTicket
            holder={data}
            source={data.deviceHasHardwareKey ? 'device' : 'server'}
            cached={cached}
            childId={childId}
          />
        ) : data.boundDeviceName ? (
          <>
            <TicketCard ticket={data}>
              <Notice
                icon="⇄"
                tint={brand.blue}
                title={`${whose} staat op een ander toestel`}
                body={`${whose} is gekoppeld aan ${data.boundDeviceName}. Wil je het op dit toestel gebruiken? Dan werkt de code op het andere toestel niet meer.`}
                action={
                  data.rebindsLeft > 0
                    ? {
                        label: binding ? 'Even geduld…' : 'Op dit toestel gebruiken',
                        onPress: () => void bind(),
                        disabled: binding,
                      }
                    : undefined
                }
              />
            </TicketCard>
            {bindError ? (
              <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
                {bindError}
              </AppText>
            ) : null}
            <InfoRows
              bullet
              rows={[
                data.rebindsLeft > 0
                  ? `Nog ${data.rebindsLeft} keer overzetten mogelijk dit carnavalsjaar`
                  : 'Je hebt je ticket dit carnavalsjaar al 3 keer overgezet',
                'Daarna kan het bestuur je helpen',
                'Kwijt of gestolen? Meld het bij het bestuur',
              ]}
            />
            <AppText variant="caption" color={colors.textSecondary}>
              Zo kan één ticket nooit op twee telefoons tegelijk gebruikt worden.
            </AppText>
          </>
        ) : (
          <View style={styles.center}>
            <ActivityIndicator accessibilityLabel="Koppelen aan dit toestel" />
            {bindError ? (
              <>
                <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
                  {bindError}
                </AppText>
                <Button label="Opnieuw proberen" onPress={() => void bind()} />
              </>
            ) : null}
          </View>
        )}
      </View>
    </Screen>
  );
}

interface Holder {
  /** Buiten carnaval: de activiteit met toegangscontrole waarvoor de QR nu geldt. */
  accessTitle?: string | null;
  holderName: string | null;
  carnivalYearName: string | null;
  validFrom: string | null;
  validTo: string | null;
}

/** Het ticket (Figma): donkerblauwe kop met naam en geldigheid, daaronder de QR-zone of een melding. */
function TicketCard({ ticket, muted, children }: { ticket: Holder; muted?: boolean; children: React.ReactNode }) {
  return (
    <Card style={styles.ticket}>
      <View style={[styles.head, { backgroundColor: muted ? '#5B6C7B' : brand.navy }]}>
        <AppText variant="label" color={brand.yellow}>
          LEDENTICKET · CARNAVAL {ticket.carnivalYearName?.split('/').pop() ?? ''}
        </AppText>
        <AppText variant="sectionHeader" color="#FFFFFF">
          {ticket.holderName ?? 'Lid'}
        </AppText>
        <AppText variant="caption" color="rgba(255,255,255,0.85)">
          {muted
            ? 'Niet geldig'
            : ticket.accessTitle
              ? `Geldig bij ${ticket.accessTitle}`
              : `Geldig van ${validityText(ticket.validFrom, ticket.validTo)}`}
        </AppText>
      </View>
      <View style={styles.zone}>{children}</View>
    </Card>
  );
}

function Notice({
  icon,
  tint,
  title,
  body,
  action,
}: {
  icon: string;
  tint: string;
  title: string;
  body: string;
  action?: { label: string; onPress: () => void; secondary?: boolean; disabled?: boolean };
}) {
  const { colors } = useTheme();
  return (
    <View style={styles.notice}>
      <View style={[styles.icon, { backgroundColor: `${tint}24` }]}>
        <AppText variant="sectionHeader" color={tint}>
          {icon}
        </AppText>
      </View>
      <AppText variant="sectionHeader" style={styles.centerText} accessibilityRole="header">
        {title}
      </AppText>
      <AppText variant="body" color={colors.textSecondary} style={styles.centerText}>
        {body}
      </AppText>
      {action ? (
        <View style={styles.stretch}>
          <Button
            label={action.label}
            onPress={action.onPress}
            variant={action.secondary ? 'secondary' : 'primary'}
            disabled={action.disabled}
          />
        </View>
      ) : null}
    </View>
  );
}

function InfoRows({ rows, bullet }: { rows: string[]; bullet?: boolean }) {
  const { colors } = useTheme();
  return (
    <View style={styles.rows}>
      {rows.map((row) => (
        <View key={row} style={styles.row}>
          <AppText variant="bodyStrong" color={bullet ? colors.textSecondary : brand.green}>
            {bullet ? '•' : '✓'}
          </AppText>
          <AppText variant="body" style={styles.full}>
            {row}
          </AppText>
        </View>
      ))}
    </View>
  );
}

/** De live code: elke 30 seconden een nieuwe, met aftellende balk (Figma scherm 1 en 2). */
function LiveTicket({
  holder,
  source,
  cached,
  childId,
}: {
  holder: Holder | MyTicket;
  source: 'device' | 'server';
  cached: CachedTicket | null;
  childId?: string;
}) {
  const { colors } = useTheme();
  const online = useOnline();
  const [code, setCode] = useState<{ code: string; issuedAt: number } | null>(null);
  const [failed, setFailed] = useState(false);
  const [now, setNow] = useState(() => Date.now());
  const busy = useRef(false);

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);

  const age = code ? now / 1000 - code.issuedAt : Number.POSITIVE_INFINITY;
  const due = age >= REFRESH_SECONDS;
  useEffect(() => {
    if (!due || busy.current) return;
    busy.current = true;
    const next = source === 'device' && cached ? deviceCode(cached, new Date()) : serverCode(childId);
    next
      .then((result) => {
        setFailed(!result);
        if (result) setCode(result);
      })
      .catch(() => setFailed(true))
      .finally(() => {
        busy.current = false;
      });
  }, [due, source, cached, childId]);

  const remaining = code ? Math.max(0, Math.ceil(REFRESH_SECONDS - age)) : 0;
  const offline = !online;
  const status =
    failed && source === 'server'
      ? 'Geen code: op dit toestel is internet nodig'
      : offline
        ? `Offline · code blijft werken (${remaining} s)`
        : `Live · ververst over ${remaining} s`;
  const statusColor = failed && source === 'server' ? brand.red : offline ? brand.yellow : brand.green;

  return (
    <>
      <TicketCard ticket={holder}>
        <View
          style={styles.qr}
          accessible
          accessibilityLabel="QR-code van je ledenticket. Laat deze scannen bij de ingang."
        >
          {code && !(failed && source === 'server') ? (
            <QRCode value={code.code} size={240} ecl="M" color={brand.navy} backgroundColor="#FFFFFF" quietZone={0} />
          ) : (
            <ActivityIndicator accessibilityLabel="Code maken" />
          )}
        </View>
        <View style={styles.live} accessibilityLiveRegion="polite">
          <View style={[styles.dot, { backgroundColor: statusColor }]} />
          <AppText variant="bodyStrong">{status}</AppText>
        </View>
        <View style={[styles.bar, { backgroundColor: colors.border }]}>
          <View
            style={[styles.barFill, { width: `${(remaining / REFRESH_SECONDS) * 100}%`, backgroundColor: statusColor }]}
          />
        </View>
      </TicketCard>
      <InfoRows
        rows={[
          childId ? 'Op jouw telefoon, als ouder/verzorger' : 'Gekoppeld aan dit toestel',
          source === 'device' ? 'Werkt ook zonder internet' : 'Code van de server: internet nodig',
          'Scherm staat op maximale helderheid',
        ]}
      />
      <AppText variant="caption" color={colors.textSecondary}>
        Laat de code scannen bij de ingang. Een screenshot werkt niet: de code verandert elke 30 seconden.
      </AppText>
    </>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16, paddingBottom: 24 },
  loading: { marginTop: 48 },
  center: { alignItems: 'center', gap: 12, paddingVertical: 32 },
  ticket: { padding: 0, overflow: 'hidden', borderRadius: 20 },
  head: { paddingHorizontal: 20, paddingVertical: 16, gap: 4 },
  zone: { backgroundColor: '#FFFFFF', padding: 20, alignItems: 'center', gap: 12 },
  qr: { width: 240, height: 240, alignItems: 'center', justifyContent: 'center' },
  live: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  dot: { width: 10, height: 10, borderRadius: 5 },
  bar: { width: 240, height: 4, borderRadius: 2, overflow: 'hidden' },
  barFill: { height: 4, borderRadius: 2 },
  notice: { alignItems: 'center', gap: 12, paddingTop: 8, alignSelf: 'stretch' },
  icon: { width: 64, height: 64, borderRadius: 32, alignItems: 'center', justifyContent: 'center' },
  centerText: { textAlign: 'center' },
  full: { flex: 1 },
  stretch: { alignSelf: 'stretch' },
  rows: { gap: 10 },
  row: { flexDirection: 'row', gap: 10, alignItems: 'flex-start' },
});
