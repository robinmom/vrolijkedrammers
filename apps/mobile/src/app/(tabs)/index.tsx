import { brand, radius } from '@drammers/design-tokens';
import { LinearGradient } from 'expo-linear-gradient';
import { router } from 'expo-router';
import { useState } from 'react';
import { Image, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { queryKeys, useEvents, useNews, useWebsiteHero } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { useSessionStatus } from '../../auth/useSession';
import { useUnreadCount } from '../../features/badges';
import { dateBlockParts, greeting, newsDateLong, startTime } from '../../lib/dates';
import { useTheme } from '../../theme/ThemeProvider';
import { useHeroStatusBar } from '../../theme/useHeroStatusBar';
import { AppText, EmptyState, EventCard, HeroButton, NewsRow, QueryState, RemoteImage, Screen, SectionHeader, ShortcutTile } from '../../ui';

/** Het logo van de vereniging in een witte cirkel (zelfde als het app-icoon op Android). */
const logo = require('../../../assets/images/logo.png');

/**
 * 01 Home (Figma 3:2): hero met de foto van de website (portal → Website → Homepage) onder een donkerblauw verloop,
 * het logo, de groet; daaronder eerstvolgende activiteit, snelkoppelingen en laatste nieuws. Sinds fase 21f zonder
 * aftelkaart en mascotte, zoals in het ontwerp.
 */
export default function HomeScreen() {
  useHeroStatusBar();
  const insets = useSafeAreaInsets();
  const { colors } = useTheme();
  const hero = useWebsiteHero();
  const events = useEvents();
  const news = useNews();
  const refresh = useRefresh([queryKeys.websiteHero, queryKeys.events, queryKeys.news, queryKeys.myNotifications]);
  const unread = useUnreadCount();
  const signedIn = useSessionStatus() === 'signedIn';
  // Moment van openen: voor de groet.
  const [openedAt] = useState(() => new Date());

  const next = events.data?.[0];
  const latest = news.data?.[0];

  return (
    <Screen hero {...refresh}>
      <View style={styles.hero}>
        {hero.data?.imageUrl ? <RemoteImage uri={hero.data.imageUrl} cacheKey="website-hero" style={StyleSheet.absoluteFill} /> : null}
        <LinearGradient
          colors={['rgba(18,48,71,0.75)', 'rgba(18,48,71,0.55)', 'rgba(18,48,71,0.85)']}
          locations={[0, 0.6, 1]}
          style={StyleSheet.absoluteFill}
        />
        {/* Ruimte voor de statusbalk (tijd, wifi, batterij); daaronder de inhoud zoals in Figma. */}
        <View style={[styles.heroContent, { paddingTop: insets.top + 8 }]}>
          <View style={styles.brandRow}>
            <Image source={logo} style={styles.logo} resizeMode="contain" accessibilityIgnoresInvertColors />
            <View style={styles.brandText}>
              <AppText variant="appName" color="#FFFFFF" accessibilityRole="header">
                De Vrolijke Drammers
              </AppText>
              <AppText variant="label" color="rgba(255,255,255,0.8)" style={styles.regular}>
                Loil · sinds 1958
              </AppText>
            </View>
            <HeroButton icon="meldingen" iconSize={22} opacity={0.16} accessibilityLabel="Meldingen" badge={unread} onPress={() => router.push('/meldingen')} />
          </View>

          <View style={styles.greeting}>
            <AppText variant="heroTitle" color="#FFFFFF">
              {greeting(openedAt)}, Drammer!
            </AppText>
            <AppText variant="caption" color="rgba(255,255,255,0.85)" style={styles.subtitle}>
              Alaaf! Het feest komt eraan 🎉
            </AppText>
          </View>
        </View>
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
          {signedIn ? (
            <>
              {/* Fase 19b: voor leden QR code en Munten op de plek van Uitslagen en Meldingen (die staan onder Meer). */}
              <ShortcutTile icon="qr" label="QR code" tint="yellow" onPress={() => router.push('/mijn-qr')} />
              <ShortcutTile icon="munten" label="Munten" tint="red" onPress={() => router.push('/munten')} />
            </>
          ) : (
            <>
              {/* Gasten zien geen onderdelen die alleen voor leden zijn: de tegels uit het ontwerp (Figma 3:2). */}
              <ShortcutTile icon="uitslagen" label="Uitslagen" tint="yellow" onPress={() => router.push('/uitslagen')} />
              <ShortcutTile icon="meldingen" label="Meldingen" tint="red" onPress={() => router.push('/meldingen')} />
            </>
          )}
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
    overflow: 'hidden',
  },
  heroContent: { paddingHorizontal: 20, paddingBottom: 44, gap: 20 },
  brandRow: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  logo: { width: 44, height: 44 },
  brandText: { flex: 1 },
  regular: { fontFamily: 'Inter_400Regular' },
  greeting: { gap: 6 },
  subtitle: { fontSize: 14 },
  content: { padding: 20, gap: 14 },
  shortcuts: { flexDirection: 'row', gap: 10 },
});
