import { LinearGradient } from 'expo-linear-gradient';
import { Pressable, StyleSheet, useWindowDimensions, View } from 'react-native';
import { AppText } from './AppText';
import { RemoteImage } from './RemoteImage';

interface AlbumCardProps {
  id: string;
  title: string;
  photoCount: number;
  coverUrl: string | null;
  onPress: () => void;
}

const photoLabel = (count: number) => (count === 1 ? '1 foto' : `${count} foto's`);

/** Albumkaart in de carrousel (Figma 07): 168 × 210, titel op een donkere verloop onderin. */
export function AlbumCard({ id, title, photoCount, coverUrl, onPress }: AlbumCardProps) {
  return (
    <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={`Album ${title}, ${photoLabel(photoCount)}`} style={styles.album} testID="album-card">
      <RemoteImage uri={coverUrl} cacheKey={`album-${id}`} style={StyleSheet.absoluteFill} />
      <LinearGradient colors={['rgba(18,48,71,0)', 'rgba(18,48,71,0.9)']} locations={[0.4, 1]} style={StyleSheet.absoluteFill} />
      <AppText variant="sectionHeader" color="#FFFFFF" style={styles.albumTitle} numberOfLines={2}>
        {title}
      </AppText>
      <AppText variant="label" color="rgba(255,255,255,0.85)" style={styles.albumCount}>
        {photoLabel(photoCount)}
      </AppText>
    </Pressable>
  );
}

interface GridPhoto {
  id: string;
  thumbnailUrl: string;
  caption: string | null;
}

/** Fotoraster over de volle breedte: 3 kolommen met 2 pt tussenruimte (Figma 07). */
export function PhotoGrid({ photos, onPress }: { photos: GridPhoto[]; onPress: (photoId: string) => void }) {
  const { width } = useWindowDimensions();
  const size = Math.floor((width - 4) / 3);
  return (
    <View style={styles.grid}>
      {photos.map((photo, index) => (
        <Pressable
          key={photo.id}
          onPress={() => onPress(photo.id)}
          accessibilityRole="imagebutton"
          testID="photo"
          accessibilityLabel={photo.caption ?? `Foto ${index + 1} van ${photos.length}`}
        >
          <RemoteImage uri={photo.thumbnailUrl} cacheKey={`photo-thumb-${photo.id}`} style={{ width: size, height: size }} />
        </Pressable>
      ))}
    </View>
  );
}

export { photoLabel };

const styles = StyleSheet.create({
  album: { width: 168, height: 210, borderRadius: 18, overflow: 'hidden', justifyContent: 'flex-end', padding: 14, gap: 2 },
  albumTitle: { fontSize: 15 },
  albumCount: { fontFamily: 'Inter_400Regular' },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: 2 },
});
