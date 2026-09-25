import { router, useLocalSearchParams } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { queryKeys, useAlbumPhotos, usePhotoAlbum } from '../../../api/queries';
import { useRefresh } from '../../../api/useRefresh';
import { isGuid } from '../../../lib/ids';
import { useTheme } from '../../../theme/ThemeProvider';
import { AppText, BackLink, EmptyState, LargeTitleHeader, PhotoGrid, photoLabel, QueryState, Screen } from '../../../ui';

/** Album: alle zichtbare foto's in een raster (uitbreiding van Figma 07). */
export default function AlbumScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const valid = isGuid(id);
  const albumId = valid ? id : undefined;
  const { colors } = useTheme();
  const album = usePhotoAlbum(albumId);
  const photos = useAlbumPhotos(albumId);
  const refresh = useRefresh(albumId ? [queryKeys.album(albumId), queryKeys.photos(albumId)] : []);

  if (!valid) {
    return (
      <Screen>
        <BackLink label="Foto's" />
        <EmptyState title="Album niet gevonden" />
      </Screen>
    );
  }

  return (
    <Screen {...refresh}>
      <BackLink label="Foto's" />
      {album.data ? (
        <>
          <LargeTitleHeader title={album.data.title} subtitle={photoLabel(album.data.photoCount)} />
          {album.data.description ? (
            <View style={styles.description}>
              <AppText variant="body" color={colors.textSecondary}>
                {album.data.description}
              </AppText>
            </View>
          ) : null}
          {photos.data ? (
            photos.data.length > 0 ? (
              <PhotoGrid photos={photos.data} onPress={(photoId) => router.push(`/fotos/${id}/${photoId}`)} />
            ) : (
              <EmptyState icon="fotos" title="Nog geen foto's in dit album" />
            )
          ) : (
            <QueryState query={photos} />
          )}
        </>
      ) : (
        <QueryState query={album} notFoundTitle="Album niet gevonden" />
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  description: { paddingHorizontal: 20, paddingBottom: 12 },
});
