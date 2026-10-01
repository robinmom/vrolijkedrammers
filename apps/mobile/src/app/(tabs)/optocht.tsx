import { brand, radius } from '@drammers/design-tokens';
import { LinearGradient } from 'expo-linear-gradient';
import { router } from 'expo-router';
import { Image, Share, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { queryKeys, useArrivalTimes, useCarnivalYear, useMe, useMyRegistrations, useParade } from '../../api/queries';
import { useSessionStatus } from '../../auth/useSession';
import { statusLabels } from '../../features/parade';
import { useRefresh } from '../../api/useRefresh';
import { optocht } from '../../content/static';
import { carnivalMidnight, fullDate } from '../../lib/dates';
import { openInMaps } from '../../lib/calendar';
import { useTheme } from '../../theme/ThemeProvider';
import { useHeroStatusBar } from '../../theme/useHeroStatusBar';
import { AppText, Badge, Button, Card, HeroButton, Screen } from '../../ui';

const hero = require('../../../assets/images/optocht-hero.jpg');

const DAY = 24 * 60 * 60 * 1000;

/**
 * 04 Optocht (Figma 5:174). Datum, starttijd, startlocatie en route komen uit het portal (Optocht en categorieën); zonder
 * optocht valt de datum terug op het carnavalsjaar. Inschrijven (fase 11): één inschrijving per persoon, dus met een
 * inschrijving wordt de knop "Mijn inschrijving"; "Aanmelden optocht" (groepsverantwoordelijken en gasten) alleen tijdens de
 * inschrijfperiode; leden zonder dat recht gaan naar de informatiepagina uit het portal.
 */
export default function OptochtScreen() {
  useHeroStatusBar();
  const insets = useSafeAreaInsets();
  const { colors } = useTheme();
  const year = useCarnivalYear();
  const refresh = useRefresh([queryKeys.carnivalYear, queryKeys.parade, queryKeys.myRegistrations]);
  const session = useSessionStatus();
  const me = useMe();
  const parade = useParade();
  const arrivals = useArrivalTimes();
  const canRegister = (me.data?.permissions ?? []).includes('parade.register');
  // Fase 22b: juryleden jureren vanuit de optocht.
  const canJudge = (me.data?.permissions ?? []).includes('parade.judge');
  const registrations = useMyRegistrations(canRegister);
  const guest = session === 'signedOut';
  const open = parade.data?.registrationOpen ?? false;

  const p = parade.data;
  // Alleen de inschrijving voor de huidige optocht; die van vorige optochten horen hier niet (ook al hebben ze een startnummer).
  const mine = canRegister ? registrations.data?.find((r) => r.paradeId === p?.id && r.status !== 'Withdrawn') : undefined;

  // Eén knop, afhankelijk van de situatie; buiten de inschrijfperiode geen aanmeldknop.
  const action = mine
    ? { label: 'Mijn inschrijving', icon: 'optocht' as const, onPress: () => router.push(`/optocht/${mine.id}`) }
    : session === 'signedIn' && me.data && !canRegister
      ? { label: 'Meedoen aan de optocht', icon: 'megafoon' as const, onPress: () => router.push('/optocht/info') }
      : open && (guest || canRegister) && (guest || registrations.isSuccess)
        ? {
            label: 'Aanmelden optocht',
            icon: 'plus' as const,
            onPress: () => router.push(guest ? { pathname: '/optocht/inschrijven', params: { gast: '1' } } : '/optocht/inschrijven'),
          }
        : null;
  const registrationNote = !parade.data
    ? null
    : open
      ? `Inschrijven kan tot ${fullDate(parade.data.registrationClosesAt).toLowerCase()}.`
      : new Date(parade.data.registrationOpensAt) > new Date()
        ? `De inschrijving opent op ${fullDate(parade.data.registrationOpensAt).toLowerCase()}.`
        : 'De inschrijving is gesloten.';

  // Midden op de dag rekenen, zodat de tijdzone de datum nooit verschuift.
  const date = p
    ? fullDate(`${p.paradeDate}T12:00:00Z`)
    : year.data
      ? fullDate(new Date(carnivalMidnight(year.data.carnivalStartDate).getTime() + optocht.dayOffset * DAY + DAY / 2).toISOString())
      : null;
  const startTime = p ? p.startTime.slice(0, 5) : null;
  const routeLength = p?.routeLengthKm != null ? `${String(p.routeLengthKm).replace('.', ',')} km` : null;
  const routeQuery = p?.startLocation ? `${p.startLocation}, Loil` : optocht.routeQuery;

  const share = () =>
    Share.share({
      message: `${p?.name ?? 'Optocht Loil'}${date ? ` op ${date.toLowerCase()}` : ''}${startTime ? `, start ${startTime} uur` : ''}. Alaaf! – De Vrolijke Drammers`,
    });

  return (
    <Screen hero {...refresh}>
      <View style={styles.hero}>
        <Image source={hero} style={StyleSheet.absoluteFill} resizeMode="cover" accessibilityIgnoresInvertColors />
        <LinearGradient
          colors={['rgba(18,48,71,0.6)', 'rgba(18,48,71,0)', 'rgba(18,48,71,0.92)']}
          locations={[0, 0.4, 1]}
          style={StyleSheet.absoluteFill}
        />
        <View style={[styles.heroTop, { paddingTop: insets.top + 4 }]}>
          <HeroButton icon="delen" opacity={0.2} accessibilityLabel="Deel de optocht" onPress={share} />
        </View>
        <View style={styles.heroText}>
          {date ? (
            <View style={styles.pill}>
              <Badge label={date.toUpperCase()} variant="highlight" size="regular" />
            </View>
          ) : null}
          <AppText variant="largeTitle" color="#FFFFFF" style={styles.title} accessibilityRole="header">
            {p?.name ?? 'Optocht Loil'}
          </AppText>
          <AppText variant="body" color="rgba(255,255,255,0.9)">
            {optocht.subtitle}
          </AppText>
        </View>
      </View>

      <View style={styles.content}>
        <Card style={styles.stats}>
          <Stat value={startTime ?? '–'} label="Start" accessibilityLabel={startTime ? `Start: ${startTime} uur` : 'Start: nog niet bekend'} />
          <View style={[styles.divider, { backgroundColor: colors.border }]} />
          <Stat value={routeLength ?? '–'} label="Route" accessibilityLabel={routeLength ? `Route: ${routeLength}` : 'Route: nog niet bekend'} />
        </Card>

        {canJudge ? <Button label="Jureren" icon="uitslagen" onPress={() => router.push('/jureren')} /> : null}
        <View style={styles.buttons}>
          {action ? (
            <View style={styles.flex}>
              <Button
                label={action.label}
                icon={action.icon}
                onPress={action.onPress}
                badge={mine?.status === 'AdditionalInformationRequired' ? 1 : 0}
                badgeLabel="aanvulling gevraagd"
              />
            </View>
          ) : null}
          <Button label="Route" icon="locatie" variant="secondary" onPress={() => openInMaps(routeQuery)} />
        </View>
        {arrivals.data?.published ? (
          <Button
            label="Aanrijtijden wagens"
            icon="klok"
            variant="secondary"
            onPress={() => router.push('/optocht/aanrijtijden')}
          />
        ) : null}
        {mine ? (
          <AppText variant="caption" color={colors.textSecondary}>
            {mine.groupName ?? 'Jullie groep'}: {statusLabels[mine.status].toLowerCase()}
            {mine.registrationNumber ? ` · opgavenummer ${mine.registrationNumber}` : ''}
          </AppText>
        ) : registrationNote ? (
          <AppText variant="caption" color={colors.textSecondary}>
            {registrationNote}
          </AppText>
        ) : null}

        {p ? (
          <>
            <AppText variant="sectionHeader" accessibilityRole="header">
              Programma
            </AppText>
            <Card style={styles.timeline}>
              <View style={styles.step} accessible accessibilityLabel={`${startTime} uur, start optocht${p.startLocation ? `, ${p.startLocation}` : ''}`}>
                <View style={[styles.dot, { backgroundColor: `${brand.red}40` }]}>
                  <View style={[styles.dotInner, { backgroundColor: brand.red }]} />
                </View>
                <View style={styles.flex}>
                  <AppText variant="bodyStrong">Start optocht</AppText>
                  {p.startLocation ? (
                    <AppText variant="caption" color={colors.textSecondary}>
                      {p.startLocation}
                    </AppText>
                  ) : null}
                </View>
                <AppText variant="bodyStrong" style={styles.time}>
                  {startTime}
                </AppText>
              </View>
              {p.routeDescription ? (
                <AppText variant="body" color={colors.textSecondary} style={[styles.route, { borderTopColor: colors.border }]}>
                  {p.routeDescription}
                </AppText>
              ) : null}
            </Card>
          </>
        ) : null}
      </View>
    </Screen>
  );
}

function Stat({ value, label, accent, accessibilityLabel }: { value: string; label: string; accent?: boolean; accessibilityLabel?: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.stat} accessible accessibilityLabel={accessibilityLabel ?? `${label}: ${value}`}>
      <AppText variant="dateDay" color={accent ? colors.accentText : colors.textPrimary} style={styles.statValue}>
        {value}
      </AppText>
      <AppText variant="label" color={colors.textSecondary} style={styles.regular}>
        {label}
      </AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  // overflow: 'hidden': iOS tekent een 'cover'-afbeelding anders buiten de hero door.
  hero: { height: 330, justifyContent: 'space-between', backgroundColor: brand.navy, overflow: 'hidden' },
  heroTop: { flexDirection: 'row', justifyContent: 'flex-end', paddingHorizontal: 20 },
  heroText: { paddingHorizontal: 20, paddingBottom: 24, gap: 6 },
  pill: { alignSelf: 'flex-start' },
  title: { fontSize: 34 },
  content: { paddingHorizontal: 20, paddingTop: 16, gap: 16 },
  stats: { flexDirection: 'row', alignItems: 'center', paddingHorizontal: 8, paddingVertical: 14 },
  stat: { flex: 1, alignItems: 'center', gap: 2 },
  statValue: { lineHeight: 26 },
  regular: { fontFamily: 'Inter_400Regular' },
  divider: { width: 1, height: 36 },
  buttons: { flexDirection: 'row', gap: 10 },
  flex: { flex: 1 },
  route: { borderTopWidth: 1, paddingVertical: 10 },
  timeline: { paddingHorizontal: 16, paddingVertical: 8, borderRadius: radius.md },
  step: { flexDirection: 'row', alignItems: 'center', gap: 14, paddingVertical: 10 },
  // Figma: stip van 14 met een rand van 4 op 25 %; als halo met een volle kern van 6.
  dot: { width: 14, height: 14, borderRadius: 7, alignItems: 'center', justifyContent: 'center' },
  dotInner: { width: 6, height: 6, borderRadius: 3 },
  time: { fontFamily: 'Poppins_700Bold' },
});
