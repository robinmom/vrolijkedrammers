import { radius } from '@drammers/design-tokens';
import { StyleSheet, View, type ViewProps } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';

/** Witte kaart met Figma-schaduw (0 4 16, docs/17 §2). */
export function Card({ style, ...rest }: ViewProps) {
  const { colors } = useTheme();
  return <View style={[styles.card, { backgroundColor: colors.surface, shadowColor: colors.shadow }, style]} {...rest} />;
}

const styles = StyleSheet.create({
  card: {
    borderRadius: radius.md,
    padding: 12,
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 1,
    shadowRadius: 8,
    elevation: 3,
  },
});
