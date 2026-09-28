import { brand, radius } from '@drammers/design-tokens';
import { Pressable, StyleSheet } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';
import { CountBadge, withCount } from './CountBadge';
import { Icon, type IconName } from './Icon';

interface ButtonProps {
  label: string;
  onPress?: () => void;
  /** `primary` = rood gevuld; `secondary` = blauwe rand (Figma 04 Optocht). */
  variant?: 'primary' | 'secondary';
  icon?: IconName;
  disabled?: boolean;
  /** Rood telbolletje op de hoek, bijv. een openstaande actie ("1 actie nodig"). */
  badge?: number;
  badgeLabel?: string;
}

export function Button({ label, onPress, variant = 'primary', icon, disabled, badge = 0, badgeLabel = 'actie nodig' }: ButtonProps) {
  const { colors } = useTheme();
  const primary = variant === 'primary';
  const foreground = primary ? colors.onActionPrimary : colors.linkText;
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityState={{ disabled: Boolean(disabled) }}
      accessibilityLabel={badge > 0 ? withCount(label, badge, badgeLabel) : undefined}
      style={({ pressed }) => [
        styles.button,
        primary ? { backgroundColor: brand.red } : { borderWidth: 1.5, borderColor: colors.linkText },
        (pressed || disabled) && styles.dimmed,
      ]}
    >
      {icon ? <Icon name={icon} size={20} color={foreground} /> : null}
      <AppText variant="bodyStrong" color={foreground}>
        {label}
      </AppText>
      <CountBadge count={badge} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    minHeight: 50,
    borderRadius: radius.sm,
    paddingHorizontal: 18,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
  },
  dimmed: { opacity: 0.7 },
});
