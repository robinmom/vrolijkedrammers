import { brand, radius } from '@drammers/design-tokens';
import { StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';

export type BadgeVariant = 'youth' | 'highlight' | 'category';

interface BadgeProps {
  label: string;
  variant: BadgeVariant;
  /** `small` = in lijsten (Figma 02), `regular` = op detailschermen (Figma 06). */
  size?: 'small' | 'regular';
}

/** Labels zoals "Jeugd", "Hoogtepunt" (Figma 02) en de categorie ("CARNAVAL", "OPTOCHT", Figma 03/06). */
export function Badge({ label, variant, size = 'small' }: BadgeProps) {
  const { colors } = useTheme();
  const background = { youth: colors.tintGreen, highlight: brand.yellow, category: colors.tintRed }[variant];
  const foreground = { youth: colors.successText, highlight: brand.navy, category: colors.accentText }[variant];
  const padding = size === 'regular' ? styles.regular : variant === 'category' ? styles.category : styles.small;
  return (
    <View style={[styles.badge, padding, { backgroundColor: background }]}>
      <AppText variant="overline" color={foreground} style={variant !== 'category' && styles.text}>
        {label}
      </AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  badge: { borderRadius: radius.pill },
  small: { paddingHorizontal: 8, paddingVertical: 2 },
  category: { paddingHorizontal: 10, paddingVertical: 3 },
  regular: { paddingHorizontal: 10, paddingVertical: 4 },
  text: { textTransform: 'none' },
});
