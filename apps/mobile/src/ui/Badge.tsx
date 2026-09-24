import { brand, radius } from '@drammers/design-tokens';
import { StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';

export type BadgeVariant = 'youth' | 'highlight';

/** Labels zoals "Jeugd" en "Hoogtepunt" (Figma 02 Programma). */
export function Badge({ label, variant }: { label: string; variant: BadgeVariant }) {
  const { colors } = useTheme();
  const background = variant === 'youth' ? colors.tintGreen : brand.yellow;
  const foreground = variant === 'youth' ? colors.successText : brand.navy;
  return (
    <View style={[styles.badge, { backgroundColor: background }]}>
      <AppText variant="overline" color={foreground} style={styles.text}>
        {label}
      </AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  badge: { borderRadius: radius.pill, paddingHorizontal: 8, paddingVertical: 2 },
  text: { textTransform: 'none' },
});
