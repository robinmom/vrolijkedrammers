import { Pressable, StyleSheet } from 'react-native';
import { Icon, type IconName } from './Icon';

interface HeroButtonProps {
  icon: IconName;
  accessibilityLabel: string;
  onPress: () => void;
  /** Figma: 16 % op de blauwe hero (Home), 20–22 % op foto's (Optocht, detail). */
  opacity?: number;
  iconSize?: number;
}

/** Rond, half-transparant knopje op een hero (terug, delen, meldingen). */
export function HeroButton({ icon, accessibilityLabel, onPress, opacity = 0.22, iconSize = 20 }: HeroButtonProps) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      hitSlop={4}
      style={({ pressed }) => [styles.button, { backgroundColor: `rgba(255,255,255,${pressed ? opacity + 0.12 : opacity})` }]}
    >
      <Icon name={icon} size={iconSize} color="#FFFFFF" />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  // 40 pt zichtbaar + hitSlop 4 = 48 pt aanraakdoel.
  button: { width: 40, height: 40, borderRadius: 20, alignItems: 'center', justifyContent: 'center' },
});
