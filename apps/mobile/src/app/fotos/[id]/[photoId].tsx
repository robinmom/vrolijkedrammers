import { Image } from 'expo-image';
import { router, useLocalSearchParams } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { useState } from 'react';
import { FlatList, StyleSheet, useWindowDimensions, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import type { Photo } from '../../../api/client';
import { useAlbumPhotos } from '../../../api/queries';
import { isGuid } from '../../../lib/ids';
import { AppText, HeroButton, QueryState } from '../../../ui';

const close = () => (router.canGoBack() ? router.back() : router.replace('/fotos'));

/** Fotoviewer: vegen tussen de foto's van een album, met bijschrift en fotograaf. */
export default function FotoViewer() {
  const { id, photoId } = useLocalSearchParams<{ id: string; photoId: string }>();
  const photos = useAlbumPhotos(isGuid(id) ? id : undefined);
  return (
    <SafeAreaView style={styles.container}>
      <StatusBar style="light" />
      {photos.data ? (
        <Viewer photos={photos.data} start={Math.max(0, photos.data.findIndex((p) => p.id === photoId))} />
      ) : (
        <>
          <View style={styles.top}>
            <HeroButton icon="terug" accessibilityLabel="Sluiten" onPress={close} />
          </View>
          <QueryState query={photos} />
        </>
      )}
    </SafeAreaView>
  );
}

function Viewer({ photos, start }: { photos: Photo[]; start: number }) {
  const { width } = useWindowDimensions();
  const [index, setIndex] = useState(start);
  const current = photos[index];
  return (
    <>
      <View style={styles.top}>
        <HeroButton icon="terug" accessibilityLabel="Sluiten" onPress={close} />
        <AppText variant="label" color="rgba(255,255,255,0.85)">
          {index + 1} / {photos.length}
        </AppText>
      </View>
      <FlatList
        data={photos}
        horizontal
        pagingEnabled
        initialScrollIndex={start}
        getItemLayout={(_, i) => ({ length: width, offset: width * i, index: i })}
        showsHorizontalScrollIndicator={false}
        keyExtractor={(p) => p.id}
        onMomentumScrollEnd={(e) => setIndex(Math.round(e.nativeEvent.contentOffset.x / width))}
        renderItem={({ item, index: i }) => (
          <Image
            source={{ uri: item.displayUrl, cacheKey: `photo-display-${item.id}` }}
            placeholder={{ uri: item.thumbnailUrl, cacheKey: `photo-thumb-${item.id}` }}
            cachePolicy="disk"
            contentFit="contain"
            style={{ width, flex: 1 }}
            accessibilityLabel={item.caption ?? `Foto ${i + 1} van ${photos.length}`}
          />
        )}
      />
      {current?.caption || current?.photographer ? (
        <View style={styles.caption}>
          {current.caption ? (
            <AppText variant="body" color="#FFFFFF">
              {current.caption}
            </AppText>
          ) : null}
          {current.photographer ? (
            <AppText variant="label" color="rgba(255,255,255,0.75)">
              Foto: {current.photographer}
            </AppText>
          ) : null}
        </View>
      ) : null}
    </>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#000000' },
  top: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingHorizontal: 20, paddingVertical: 8 },
  caption: { paddingHorizontal: 20, paddingVertical: 12, gap: 4 },
});
