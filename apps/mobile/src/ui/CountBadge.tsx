import { brand } from '@drammers/design-tokens';
import { StyleSheet, Text, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';

/**
 * Rood telbolletje zoals op een iOS-app-icoon (ongelezen meldingen, een actie bij de optocht). Leesbaar gemaakt via
 * het label van de knop eromheen; zelf verborgen voor de schermlezer. Niets tonen bij 0.
 */
export function CountBadge({ count, style }: { count: number; style?: object }) {
  const { colors } = useTheme();
  if (count <= 0) {
    return null;
  }
  return (
    <View
      style={[styles.badge, { borderColor: colors.canvas }, style]}
      accessibilityElementsHidden
      importantForAccessibility="no-hide-descendants"
      pointerEvents="none"
    >
      <Text style={styles.text} allowFontScaling={false}>
        {count > 99 ? '99+' : count}
      </Text>
    </View>
  );
}

/** Tekst voor schermlezers: "Meldingen, 2 ongelezen". */
export const withCount = (label: string, count: number, noun = 'ongelezen') =>
  count > 0 ? `${label}, ${count} ${noun}` : label;

const styles = StyleSheet.create({
  badge: {
    position: 'absolute',
    top: -6,
    right: -6,
    minWidth: 22,
    height: 22,
    paddingHorizontal: 5,
    borderRadius: 11,
    borderWidth: 2,
    backgroundColor: brand.red,
    alignItems: 'center',
    justifyContent: 'center',
  },
  text: { color: '#FFFFFF', fontSize: 12, fontWeight: '700', lineHeight: 15 },
});
