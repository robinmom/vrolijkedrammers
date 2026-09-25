import { brand, radius } from '@drammers/design-tokens';
import { LinearGradient } from 'expo-linear-gradient';
import { Alert, Image, Share, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { queryKeys, useCarnivalYear } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { optocht, placeholder } from '../../content/static';
import { carnivalMidnight, fullDate } from '../../lib/dates';
import { openInMaps } from '../../lib/calendar';
import { useTheme } from '../../theme/ThemeProvider';
import { useHeroStatusBar } from '../../theme/useHeroStatusBar';
import { AppText, Badge, Button, Card, HeroButton, Screen } from '../../ui';

const hero = require('../../../assets/images/optocht-hero.jpg');

const DAY = 24 * 60 * 60 * 1000;
const dotColors = { blue: brand.blue, red: brand.red, yellow: brand.yellow, green: brand.green } as const;

/**
 * 04 Optocht (Figma 5:174). Publieke optochtinformatie; de datum volgt uit het actieve carnavalsjaar.
 * Tijden en route zijn voorlopig (content/static.ts); inschrijven en deelnemersaantallen volgen in fase 11.
 */
export default function OptochtScreen() {
  useHeroStatusBar();
  const insets = useSafeAreaInsets();
  const { colors } = useTheme();
  const year = useCarnivalYear();
  const refresh = useRefresh([queryKeys.carnivalYear]);

  // Midden op de dag rekenen, zodat de tijdzone de datum nooit verschuift.
  const date = year.data ? fullDate(new Date(carnivalMidnight(year.data.carnivalStartDate).getTime() + optocht.dayOffset * DAY + DAY / 2).toISOString()) : null;

  const share = () =>
    Share.share({ message: `Optocht Loil${date ? ` op ${date.toLowerCase()}` : ''}, start ${optocht.startTime} uur. Alaaf! – De Vrolijke Drammers` });

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
            Optocht Loil
          </AppText>
          <AppText variant="body" color="rgba(255,255,255,0.9)">
            {optocht.subtitle}
          </AppText>
        </View>
      </View>

      <View style={styles.content}>
        <Card style={styles.stats}>
          <Stat value={optocht.startTime} label="Start" />
          <View style={[styles.divider, { backgroundColor: colors.border }]} />
          <Stat value="–" label="Deelnemers" accent accessibilityLabel="Deelnemers: nog niet bekend" />
          <View style={[styles.divider, { backgroundColor: colors.border }]} />
          <Stat value={optocht.routeLength} label="Route" />
        </Card>

        <View style={styles.buttons}>
          <View style={styles.flex}>
            <Button
              label="Groep inschrijven"
              icon="plus"
              onPress={() => Alert.alert('Groep inschrijven', 'De inschrijving voor de optocht opent binnenkort in de app.')}
            />
          </View>
          <Button label="Route" icon="locatie" variant="secondary" onPress={() => openInMaps(optocht.routeQuery)} />
        </View>

        <AppText variant="sectionHeader" accessibilityRole="header">
          Tijdlijn
        </AppText>
        <Card style={styles.timeline}>
          {optocht.timeline.map((step, index) => (
            <View
              key={step.title}
              style={[styles.step, index < optocht.timeline.length - 1 && { borderBottomWidth: 1, borderBottomColor: colors.border }]}
              accessible
              accessibilityLabel={`${step.time} uur, ${step.title}, ${step.place}`}
            >
              <View style={[styles.dot, { backgroundColor: `${dotColors[step.color]}40` }]}>
                <View style={[styles.dotInner, { backgroundColor: dotColors[step.color] }]} />
              </View>
              <View style={styles.flex}>
                <AppText variant="bodyStrong">{step.title}</AppText>
                <AppText variant="caption" color={colors.textSecondary}>
                  {step.place}
                </AppText>
              </View>
              <AppText variant="bodyStrong" style={styles.time}>
                {step.time}
              </AppText>
            </View>
          ))}
        </Card>
        {placeholder ? (
          <AppText variant="label" color={colors.textSecondary} style={styles.note}>
            Voorlopige tijden; het definitieve schema volgt.
          </AppText>
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
  timeline: { paddingHorizontal: 16, paddingVertical: 8, borderRadius: radius.md },
  step: { flexDirection: 'row', alignItems: 'center', gap: 14, paddingVertical: 10 },
  // Figma: stip van 14 met een rand van 4 op 25 %; als halo met een volle kern van 6.
  dot: { width: 14, height: 14, borderRadius: 7, alignItems: 'center', justifyContent: 'center' },
  dotInner: { width: 6, height: 6, borderRadius: 3 },
  time: { fontFamily: 'Poppins_700Bold' },
  note: { textAlign: 'center' },
});
