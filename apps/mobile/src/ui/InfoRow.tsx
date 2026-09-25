import { brand, radius } from '@drammers/design-tokens';
import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';
import { Icon, type IconName } from './Icon';
import type { TileTint } from './ShortcutTile';

interface InfoRowProps {
  icon: IconName;
  tint: TileTint;
  title: string;
  detail?: string | null;
  /** Maakt de detailregel een blauwe link ("Toevoegen aan agenda", adres → kaart). */
  onDetailPress?: () => void;
  detailAccessibilityLabel?: string;
}

/** Regel met gekleurd icoonvlak, titel en detail (Figma 06 Activiteit detail). */
export function InfoRow({ icon, tint, title, detail, onDetailPress, detailAccessibilityLabel }: InfoRowProps) {
  const { colors, mode } = useTheme();
  const bubble = { red: colors.tintRed, blue: colors.tintBlue, yellow: colors.tintYellow, green: colors.tintGreen }[tint];
  const iconColor = { red: brand.red, blue: mode === 'dark' ? '#5AB0E6' : brand.blue, yellow: brand.yellow, green: brand.green }[tint];
  return (
    <View style={styles.row}>
      <View style={[styles.bubble, { backgroundColor: bubble }]}>
        <Icon name={icon} size={22} color={iconColor} />
      </View>
      <View style={styles.text}>
        <AppText variant="bodyStrong">{title}</AppText>
        {detail && onDetailPress ? (
          <Pressable onPress={onDetailPress} accessibilityRole="link" accessibilityLabel={detailAccessibilityLabel ?? detail} hitSlop={10}>
            <AppText variant="caption" color={colors.linkText} style={styles.link}>
              {detail}
            </AppText>
          </Pressable>
        ) : detail ? (
          <AppText variant="caption" color={colors.textSecondary}>
            {detail}
          </AppText>
        ) : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: 14 },
  bubble: { width: 44, height: 44, borderRadius: radius.sm, alignItems: 'center', justifyContent: 'center' },
  text: { flex: 1, gap: 2 },
  link: { fontFamily: 'Inter_500Medium' },
});
