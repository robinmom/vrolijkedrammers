import { router } from 'expo-router';
import { ScrollView, StyleSheet, View } from 'react-native';
import { queryKeys, useAlbumPhotos, usePhotoAlbums } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { useTheme } from '../../theme/ThemeProvider';
import { AlbumCard, AppText, BackLink, EmptyState, LargeTitleHeader, PhotoGrid, QueryState, Screen, SectionHeader } from '../../ui';

const RECENT = 15;

/** 07 Foto's (Figma 8:428): albumcarrousel en de nieuwste foto's van het laatste album. */
export default function FotosScreen() {
  const { colors } = useTheme();
  const albums = usePhotoAlbums();
  const latest = albums.data?.[0];
  const photos = useAlbumPhotos(latest?.id);
  const refresh = useRefresh([queryKeys.albums, ...(latest ? [queryKeys.photos(latest.id)] : [])]);

  return (
    <Screen {...refresh}>
      <BackLink label="Meer" />
      <View style={styles.titleOffset}>
        <LargeTitleHeader title="Foto's" />
      </View>
      {!albums.data ? (
        <QueryState query={albums} />
      ) : albums.data.length === 0 ? (
        <EmptyState icon="fotos" title="Nog geen foto's" message="Na de eerste activiteit verschijnen hier de albums." />
      ) : (
        <View style={styles.content}>
          <View style={styles.padded}>
            <SectionHeader title="Albums" />
          </View>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.carousel}>
            {albums.data.map((album) => (
              <AlbumCard
                key={album.id}
                id={album.id}
                title={album.title}
                photoCount={album.photoCount}
                coverUrl={album.coverUrl}
                onPress={() => router.push(`/fotos/${album.id}`)}
              />
            ))}
          </ScrollView>
          {latest ? (
            <>
              <View style={styles.padded}>
                <SectionHeader title="Recent toegevoegd" linkLabel="Alles" onLinkPress={() => router.push(`/fotos/${latest.id}`)} />
                <AppText variant="label" color={colors.textSecondary} style={styles.regular}>
                  {[latest.title, latest.albumDate?.slice(0, 4)].filter(Boolean).join(' · ')}
                </AppText>
              </View>
              {photos.data ? (
                <PhotoGrid photos={photos.data.slice(0, RECENT)} onPress={(photoId) => router.push(`/fotos/${latest.id}/${photoId}`)} />
              ) : (
                <QueryState query={photos} />
              )}
            </>
          ) : null}
        </View>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  // De grote titel staat in Figma 8 pt verder naar rechts dan de terugknop (12 + 8 = 20).
  titleOffset: { marginTop: -4 },
  content: { paddingTop: 8, gap: 14 },
  padded: { paddingHorizontal: 20 },
  carousel: { paddingHorizontal: 20, gap: 12 },
  regular: { fontFamily: 'Inter_400Regular' },
});
