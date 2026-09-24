import { brand, radius } from '@drammers/design-tokens';
import { StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';

interface DateBlockProps {
  weekday: string;
  day: string;
  month: string;
  /** `solid` = rood vlak (Home, hoogtepunt); `tinted` = lichtrood vlak (Programma). */
  variant?: 'solid' | 'tinted';
}

export function DateBlock({ weekday, day, month, variant = 'solid' }: DateBlockProps) {
  const { colors } = useTheme();
  const solid = variant === 'solid';
  const foreground = solid ? '#FFFFFF' : colors.accentText;
  return (
    <View
      style={[styles.block, { backgroundColor: solid ? brand.red : colors.tintRed }]}
      accessible
      accessibilityLabel={`${weekday} ${day} ${month}`}
    >
      <AppText variant="overline" color={foreground} style={styles.small}>
        {weekday}
      </AppText>
      <AppText variant="dateDay" color={foreground}>
        {day}
      </AppText>
      <AppText variant="overline" color={foreground} style={styles.small}>
        {month}
      </AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  block: {
    width: 52,
    minHeight: 60,
    borderRadius: radius.sm,
    alignItems: 'center',
    justifyContent: 'center',
  },
  small: { fontSize: 10 },
});
