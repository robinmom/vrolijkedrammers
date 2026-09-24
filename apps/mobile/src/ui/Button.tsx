import { brand, radius } from '@drammers/design-tokens';
import { Pressable, StyleSheet } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';
import { Icon, type IconName } from './Icon';

interface ButtonProps {
  label: string;
  onPress?: () => void;
  /** `primary` = rood gevuld; `secondary` = blauwe rand (Figma 04 Optocht). */
  variant?: 'primary' | 'secondary';
  icon?: IconName;
  disabled?: boolean;
}

export function Button({ label, onPress, variant = 'primary', icon, disabled }: ButtonProps) {
  const { colors } = useTheme();
  const primary = variant === 'primary';
  const foreground = primary ? colors.onActionPrimary : colors.linkText;
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityState={{ disabled: Boolean(disabled) }}
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
