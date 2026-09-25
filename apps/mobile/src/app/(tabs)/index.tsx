import { brand, radius } from '@drammers/design-tokens';
import { router } from 'expo-router';
import { useState } from 'react';
import { Image, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { queryKeys, useCarnivalYear, useEvents, useNews } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { carnivalMidnight, dateBlockParts, greeting, newsDateLong, startTime } from '../../lib/dates';
import { useTheme } from '../../theme/ThemeProvider';
import { useHeroStatusBar } from '../../theme/useHeroStatusBar';
import { AppText, CountdownCard, EmptyState, EventCard, HeroButton, NewsRow, QueryState, Screen, SectionHeader, ShortcutTile } from '../../ui';

const logo = require('../../../assets/images/logo.png');
const mascotte = require('../../../assets/images/mascotte.png');

const DAY = 24 * 60 * 60 * 1000;

/** 01 Home (Figma 3:2): hero met countdown, eerstvolgende activiteit, snelkoppelingen en laatste nieuws. */
export default function HomeScreen() {
  useHeroStatusBar();
  const insets = useSafeAreaInsets();
  const { colors } = useTheme();
  const year = useCarnivalYear();
  const events = useEvents();
  const news = useNews();
  const refresh = useRefresh([queryKeys.carnivalYear, queryKeys.events, queryKeys.news]);
  // Moment van openen: voor de groet en of carnaval al voorbij is (de countdown tikt zelf).
  const [openedAt] = useState(() => new Date());

  const next = events.data?.[0];
  const latest = news.data?.[0];
  const carnivalStart = year.data ? carnivalMidnight(year.data.carnivalStartDate) : undefined;
  // Na de laatste carnavalsdag verdwijnt de kaart tot het bestuur het volgende carnavalsjaar actief maakt.
  const carnivalOver = year.data ? carnivalMidnight(year.data.carnivalEndDate).getTime() + DAY < openedAt.getTime() : false;

  return (
    <Screen hero {...refresh}>
      <View style={[styles.hero, { paddingTop: insets.top + 8 }]}>
        <View style={styles.brandRow}>
          <View style={styles.logoCircle}>
            <Image source={logo} style={styles.logo} resizeMode="contain" accessibilityIgnoresInvertColors />
          </View>
          <View style={styles.brandText}>
            <AppText variant="appName" color="#FFFFFF" accessibilityRole="header">
              De Vrolijke Drammers
            </AppText>
            <AppText variant="label" color="rgba(255,255,255,0.8)" style={styles.regular}>
              Loil · sinds 1958
            </AppText>
          </View>
          <HeroButton icon="meldingen" iconSize={22} opacity={0.16} accessibilityLabel="Meldingen" onPress={() => router.push('/meldingen')} />
        </View>

        <View style={styles.greetingRow}>
          <View style={styles.greetingText}>
            <AppText variant="heroTitle" color="#FFFFFF">
              {greeting(openedAt)}, Drammer!
            </AppText>
            <AppText variant="caption" color="rgba(255,255,255,0.85)" style={styles.subtitle}>
              Alaaf! Het feest komt eraan 🎉
            </AppText>
          </View>
          <Image source={mascotte} style={styles.mascotte} accessibilityIgnoresInvertColors />
        </View>

        {carnivalStart && !carnivalOver ? (
          <CountdownCard target={carnivalStart} label={`carnaval ${carnivalStart.getUTCFullYear()}`} reachedText="Alaaf! Het is carnaval! 🎉" />
        ) : null}
      </View>

      <View style={styles.content}>
        <SectionHeader title="Eerstvolgende activiteit" linkLabel="Alles" onLinkPress={() => router.push('/programma')} />
        {next ? (
          <EventCard
            title={next.title}
            date={dateBlockParts(next.startAt)}
            dateVariant="solid"
            meta="inline"
            time={next.allDay ? 'Hele dag' : startTime(next.startAt)}
            location={next.locationName ?? undefined}
            onPress={() => router.push(`/activiteit/${next.id}`)}
            testID="next-event"
          />
        ) : events.data ? (
          <EmptyState title="Nog geen activiteiten" message="Nieuwe activiteiten verschijnen hier vanzelf." />
        ) : (
          <QueryState query={events} />
        )}

        <View style={styles.shortcuts}>
          <ShortcutTile icon="fotos" label="Foto's" tint="blue" onPress={() => router.push('/fotos')} />
          <ShortcutTile icon="uitslagen" label="Uitslagen" tint="yellow" onPress={() => router.push('/uitslagen')} />
          <ShortcutTile icon="meldingen" label="Meldingen" tint="red" onPress={() => router.push('/meldingen')} />
          <ShortcutTile icon="locatie" label="Locatie" tint="green" onPress={() => router.push('/meer/locatie')} />
        </View>

        <SectionHeader title="Laatste nieuws" linkLabel="Meer" onLinkPress={() => router.push('/nieuws')} />
        {latest ? (
          <NewsRow
            variant="home"
            id={latest.id}
            title={latest.title}
            imageUrl={latest.imageUrl}
            overline={newsDateLong(latest.publishedAt)}
            onPress={() => router.push(`/nieuws/${latest.id}`)}
          />
        ) : news.data ? (
          <AppText variant="body" color={colors.textSecondary}>
            Nog geen nieuws.
          </AppText>
        ) : (
          <QueryState query={news} />
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: {
    backgroundColor: brand.blue,
    borderBottomLeftRadius: radius.hero,
    borderBottomRightRadius: radius.hero,
    paddingHorizontal: 20,
    paddingBottom: 24,
    gap: 20,
  },
  brandRow: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  logoCircle: { width: 44, height: 44, borderRadius: 22, backgroundColor: '#FFFFFF', alignItems: 'center', justifyContent: 'center' },
  logo: { width: 40, height: 40 },
  brandText: { flex: 1 },
  regular: { fontFamily: 'Inter_400Regular' },
  greetingRow: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  greetingText: { flex: 1, gap: 6 },
  subtitle: { fontSize: 14 },
  mascotte: { width: 104, height: 104, borderRadius: 52, borderWidth: 4, borderColor: 'rgba(255,255,255,0.35)' },
  content: { padding: 20, gap: 14 },
  shortcuts: { flexDirection: 'row', gap: 10 },
});
