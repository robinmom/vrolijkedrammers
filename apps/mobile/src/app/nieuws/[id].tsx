import { brand } from '@drammers/design-tokens';
import { LinearGradient } from 'expo-linear-gradient';
import { router, useLocalSearchParams } from 'expo-router';
import { Pressable, Share, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { queryKeys, useNewsItem } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { newsDateLong } from '../../lib/dates';
import { isGuid } from '../../lib/ids';
import { useTheme } from '../../theme/ThemeProvider';
import { useHeroStatusBar } from '../../theme/useHeroStatusBar';
import { AppText, BackLink, Badge, Button, EmptyState, HeroButton, QueryState, RemoteImage, RichText, Screen } from '../../ui';

const goBack = () => (router.canGoBack() ? router.back() : router.replace('/nieuws'));

/** Nieuwsbericht. Geen apart Figma-scherm: opgebouwd uit de hero van 06 en de kaartstijl van 03. */
export default function NieuwsBerichtScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  if (!isGuid(id)) {
    return (
      <Screen>
        <BackLink label="Nieuws" />
        <EmptyState title="Bericht niet gevonden" message="Dit bericht bestaat niet (meer) of is niet openbaar." />
      </Screen>
    );
  }
  return <Bericht id={id} />;
}

function Bericht({ id }: { id: string }) {
  const news = useNewsItem(id);
  const refresh = useRefresh([queryKeys.newsItem(id)]);
  const n = news.data;
  if (!n) {
    return (
      <Screen>
        <BackLink label="Nieuws" />
        <QueryState query={news} notFoundTitle="Bericht niet gevonden" />
      </Screen>
    );
  }
  const share = () => Share.share({ message: `${n.title} – De Vrolijke Drammers` });
  return (
    <Screen hero={Boolean(n.imageUrl)} {...refresh}>
      {n.imageUrl ? <Hero id={n.id} imageUrl={n.imageUrl} onShare={share} /> : <BackLink label="Nieuws" />}
      <Body category={n.category} date={newsDateLong(n.publishedAt)} title={n.title} summary={n.summary} bodyHtml={n.bodyHtml} />
      {n.album ? <AlbumPreview album={n.album} /> : null}
    </Screen>
  );
}

function Hero({ id, imageUrl, onShare }: { id: string; imageUrl: string; onShare: () => void }) {
  useHeroStatusBar();
  const insets = useSafeAreaInsets();
  return (
    <View style={styles.hero}>
      <RemoteImage uri={imageUrl} cacheKey={`news-${id}`} style={StyleSheet.absoluteFill} />
      <LinearGradient colors={['rgba(0,0,0,0.45)', 'rgba(0,0,0,0)']} locations={[0, 0.35]} style={StyleSheet.absoluteFill} />
      <View style={[styles.heroButtons, { paddingTop: insets.top + 4 }]}>
        <HeroButton icon="terug" accessibilityLabel="Terug" onPress={goBack} />
        <HeroButton icon="delen" accessibilityLabel="Deel dit bericht" onPress={onShare} />
      </View>
    </View>
  );
}

function Body({ category, date, title, summary, bodyHtml }: { category: string | null; date: string; title: string; summary: string | null; bodyHtml: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.content}>
      <View style={styles.meta}>
        {category ? <Badge label={category} variant="category" /> : null}
        <AppText variant="label" color={colors.textSecondary} style={styles.regular}>
          {date}
        </AppText>
      </View>
      <AppText variant="heroTitle" accessibilityRole="header">
        {title}
      </AppText>
      {summary ? (
        <AppText variant="bodyStrong" color={colors.textSecondary}>
          {summary}
        </AppText>
      ) : null}
      <RichText html={bodyHtml} />
    </View>
  );
}

/** Gekoppeld fotoalbum (2026-10-13): de eerste foto's en een knop naar het hele album. */
function AlbumPreview({ album }: { album: { id: string; title: string; photoCount: number; previewUrls: string[] } }) {
  const { colors } = useTheme();
  const open = () => router.push(`/fotos/${album.id}`);
  return (
    <View style={styles.album}>
      <AppText variant="sectionHeader" accessibilityRole="header">
        {"Foto's"}
      </AppText>
      <AppText variant="body" color={colors.textSecondary}>
        {album.title}
      </AppText>
      <View style={styles.thumbs}>
        {album.previewUrls.map((url, index) => (
          <Pressable
            key={url}
            onPress={open}
            accessibilityRole="imagebutton"
            accessibilityLabel={`Foto ${index + 1} uit ${album.title}`}
            style={styles.thumb}
          >
            <RemoteImage uri={url} cacheKey={`album-${album.id}-${index}`} style={StyleSheet.absoluteFill} />
          </Pressable>
        ))}
      </View>
      <Button
        label={`Bekijk alle ${album.photoCount === 1 ? 'foto' : `${album.photoCount} foto's`}`}
        variant="secondary"
        onPress={open}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  album: { paddingHorizontal: 20, paddingBottom: 24, gap: 10 },
  thumbs: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  thumb: { width: '32%', aspectRatio: 1, borderRadius: 10, overflow: 'hidden' },
  hero: { height: 280, backgroundColor: brand.navy, overflow: 'hidden' },
  heroButtons: { flexDirection: 'row', justifyContent: 'space-between', paddingHorizontal: 20 },
  content: { padding: 20, gap: 14 },
  meta: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  regular: { fontFamily: 'Inter_400Regular' },
});
