import { Image, type ImageStyle } from 'expo-image';
import type { StyleProp } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';

interface RemoteImageProps {
  /** Kortlevende SAS-link van de API (≤ 15 min). */
  uri: string | null | undefined;
  /**
   * Vaste sleutel voor de schijfcache (bijv. `event-<id>`). De SAS-query verandert per aanroep; met een vaste
   * sleutel blijft een eenmaal geladen afbeelding ook offline zichtbaar.
   */
  cacheKey: string;
  style: StyleProp<ImageStyle>;
  /** Alleen invullen als de afbeelding inhoud toevoegt; anders is ze decoratief. */
  accessibilityLabel?: string;
}

/** Afbeelding uit de API met schijfcache; zonder afbeelding een rustig vlak in plaats van een gat. */
export function RemoteImage({ uri, cacheKey, style, accessibilityLabel }: RemoteImageProps) {
  const { colors } = useTheme();
  return (
    <Image
      source={uri ? { uri, cacheKey } : null}
      cachePolicy="disk"
      contentFit="cover"
      transition={150}
      style={[{ backgroundColor: colors.tintBlue }, style]}
      accessible={Boolean(accessibilityLabel)}
      accessibilityLabel={accessibilityLabel}
    />
  );
}
