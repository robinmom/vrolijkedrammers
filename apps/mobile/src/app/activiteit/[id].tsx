import { brand } from '@drammers/design-tokens';
import { LinearGradient } from 'expo-linear-gradient';
import { router, useLocalSearchParams } from 'expo-router';
import { Image, Linking, Pressable, Share, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { queryKeys, useEvent } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { addToCalendar, openInMaps } from '../../lib/calendar';
import { fullDate, timeRange } from '../../lib/dates';
import { isGuid } from '../../lib/ids';
import { useTheme } from '../../theme/ThemeProvider';
import { useHeroStatusBar } from '../../theme/useHeroStatusBar';
import { AppText, Badge, BackLink, EmptyState, HeroButton, Icon, InfoRow, QueryState, RemoteImage, RichText, Screen } from '../../ui';

const logo = require('../../../assets/images/logo.png');

const goBack = () => (router.canGoBack() ? router.back() : router.replace('/programma'));

/** 06 Activiteit detail (Figma 8:262). De ticketkaart en "Tickets bestellen" volgen in fase 19. */
export default function ActiviteitScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  // Deeplinks zijn invoer van buiten: alleen een geldige GUID gaat naar de API.
  if (!isGuid(id)) {
    return <NotFound />;
  }
  return <Activiteit id={id} />;
}

function NotFound() {
  return (
    <Screen>
      <BackLink label="Programma" />
      <EmptyState title="Activiteit niet gevonden" message="Deze activiteit bestaat niet (meer) of is niet openbaar." />
    </Screen>
  );
}

function Activiteit({ id }: { id: string }) {
  useHeroStatusBar();
  const insets = useSafeAreaInsets();
  const { colors } = useTheme();
  const event = useEvent(id);
  const refresh = useRefresh([queryKeys.event(id)]);
  const e = event.data;

  if (!e) {
    return (
      <Screen>
        <BackLink label="Programma" />
        <QueryState query={event} notFoundTitle="Activiteit niet gevonden" />
      </Screen>
    );
  }

  const address = [e.locationName, e.locationAddress].filter(Boolean).join(', ');
  const coordinates = e.latitude != null && e.longitude != null ? { latitude: e.latitude, longitude: e.longitude } : undefined;
  const share = () => Share.share({ message: `${e.title} – ${fullDate(e.startAt)}${e.locationName ? `, ${e.locationName}` : ''}. Alaaf! – De Vrolijke Drammers` });

  return (
    <Screen hero {...refresh}>
      <View style={styles.hero}>
        {e.imageUrl ? (
          <RemoteImage uri={e.imageUrl} cacheKey={`event-${e.id}`} style={StyleSheet.absoluteFill} />
        ) : (
          <View style={[StyleSheet.absoluteFill, styles.heroFallback]}>
            <Image source={logo} style={styles.heroLogo} resizeMode="contain" accessibilityIgnoresInvertColors />
          </View>
        )}
        <LinearGradient colors={['rgba(0,0,0,0.45)', 'rgba(0,0,0,0)']} locations={[0, 0.35]} style={StyleSheet.absoluteFill} />
        <View style={[styles.heroButtons, { paddingTop: insets.top + 4 }]}>
          <HeroButton icon="terug" accessibilityLabel="Terug" onPress={goBack} />
          <HeroButton icon="delen" accessibilityLabel="Deel deze activiteit" onPress={share} />
        </View>
      </View>

      <View style={styles.content}>
        <View style={styles.badges}>
          <Badge label={e.category.name} variant="category" size="regular" />
          {e.isHighlight || e.badgeText ? <Badge label={e.badgeText ?? 'Hoogtepunt'} variant="highlight" size="regular" /> : null}
        </View>
        <AppText variant="heroTitle" accessibilityRole="header">
          {e.title}
        </AppText>

        <View style={styles.info}>
          <InfoRow
            icon="programma"
            tint="blue"
            title={fullDate(e.startAt)}
            detail="Toevoegen aan agenda"
            detailAccessibilityLabel={`${e.title} toevoegen aan agenda`}
            onDetailPress={() => addToCalendar(e)}
          />
          <InfoRow icon="klok" tint="red" title={timeRange(e.startAt, e.endAt, e.allDay)} detail={e.summary} />
          {address ? (
            <InfoRow
              icon="locatie"
              tint="green"
              title={e.locationName ?? address}
              detail={e.locationAddress ?? 'Open in kaarten'}
              detailAccessibilityLabel={`${address}, open in kaarten`}
              onDetailPress={() => openInMaps(address, coordinates)}
            />
          ) : null}
        </View>

        {e.descriptionHtml ? (
          <>
            <View style={[styles.divider, { backgroundColor: colors.border }]} />
            <AppText variant="sectionHeader" accessibilityRole="header">
              Over deze activiteit
            </AppText>
            <RichText html={e.descriptionHtml} />
          </>
        ) : null}

        {e.attachments.length > 0 ? (
          <View style={styles.attachments}>
            <AppText variant="sectionHeader" accessibilityRole="header">
              Bijlagen
            </AppText>
            {e.attachments.map((a) => (
              <Pressable key={a.id} onPress={() => Linking.openURL(a.url)} accessibilityRole="link" style={styles.attachment}>
                <Icon name="nieuws" size={20} color={colors.linkText} />
                <AppText variant="body" color={colors.linkText} style={styles.flex}>
                  {a.fileName}
                </AppText>
              </Pressable>
            ))}
          </View>
        ) : null}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: { height: 280, backgroundColor: brand.navy, overflow: 'hidden' },
  heroFallback: { backgroundColor: brand.blue, alignItems: 'center', justifyContent: 'center' },
  heroLogo: { width: 140, height: 122 },
  heroButtons: { flexDirection: 'row', justifyContent: 'space-between', paddingHorizontal: 20 },
  content: { padding: 20, gap: 18 },
  badges: { flexDirection: 'row', gap: 8, flexWrap: 'wrap' },
  info: { gap: 14 },
  divider: { height: 1 },
  attachments: { gap: 10 },
  attachment: { flexDirection: 'row', alignItems: 'center', gap: 10, minHeight: 44 },
  flex: { flex: 1 },
});
