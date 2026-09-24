import { brand } from '@drammers/design-tokens';
import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';
import { Card } from './Card';
import { Icon, type IconName } from './Icon';

export type TileTint = 'red' | 'blue' | 'yellow' | 'green';

interface ShortcutTileProps {
  icon: IconName;
  label: string;
  tint: TileTint;
  /** `compact` = Home (4 naast elkaar), `regular` = Meer (3 per rij). */
  size?: 'compact' | 'regular';
  onPress?: () => void;
}

export function ShortcutTile({ icon, label, tint, size = 'compact', onPress }: ShortcutTileProps) {
  const { colors, mode } = useTheme();
  const bubble = { red: colors.tintRed, blue: colors.tintBlue, yellow: colors.tintYellow, green: colors.tintGreen }[tint];
  const iconColor = {
    red: brand.red,
    blue: mode === 'dark' ? '#5AB0E6' : brand.blue,
    yellow: mode === 'dark' ? brand.yellow : colors.textPrimary,
    green: brand.green,
  }[tint];
  const regular = size === 'regular';
  const bubbleSize = regular ? 48 : 44;

  return (
    <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={label} style={regular ? styles.regular : styles.compact}>
      <Card style={[styles.tile, regular && styles.tileRegular]}>
        <View style={[styles.bubble, { width: bubbleSize, height: bubbleSize, borderRadius: bubbleSize / 2, backgroundColor: bubble }]}>
          <Icon name={icon} size={regular ? 24 : 22} color={iconColor} />
        </View>
        <AppText variant="label" style={regular && styles.labelRegular} numberOfLines={1}>
          {label}
        </AppText>
      </Card>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  compact: { flex: 1 },
  regular: { width: 111 },
  tile: { alignItems: 'center', gap: 8, paddingTop: 14, paddingBottom: 12, paddingHorizontal: 4 },
  tileRegular: { paddingTop: 16, paddingBottom: 14 },
  bubble: { alignItems: 'center', justifyContent: 'center' },
  labelRegular: { fontSize: 13 },
});
