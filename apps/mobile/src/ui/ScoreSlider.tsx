import { brand, radius } from '@drammers/design-tokens';
import Slider from '@react-native-community/slider';
import * as Haptics from 'expo-haptics';
import { useState } from 'react';
import { StyleSheet, View, type AccessibilityActionEvent } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';

/**
 * Slider van 0 tot en met 100 voor het jureren (fase 22b, Figma J2). De native slider van iOS en Android
 * (@react-native-community/slider): die vangt het slepen zelf af, en tijdens het slepen zet het jureerscherm het swipen
 * naar de volgende wagen uit (`onSlidingStart`/`onSlidingEnd`). Zolang er nog niets is ingevuld staat er "–" en is de
 * balk grijs. Met VoiceOver/TalkBack is het geheel één instelbaar element: omhoog of omlaag vegen = 5 erbij of eraf.
 */
export function ScoreSlider({
  label,
  value,
  onChange,
  onSlidingStart,
  onSlidingEnd,
  disabled = false,
}: {
  label: string;
  value: number | null;
  onChange: (value: number) => void;
  onSlidingStart?: () => void;
  onSlidingEnd?: () => void;
  disabled?: boolean;
}) {
  const { colors } = useTheme();
  const [dragging, setDragging] = useState<number | null>(null);
  const shown = dragging ?? value;

  const onAction = (event: AccessibilityActionEvent) => {
    const base = value ?? 50;
    if (event.nativeEvent.actionName === 'increment') onChange(Math.min(100, value === null ? base : base + 5));
    if (event.nativeEvent.actionName === 'decrement') onChange(Math.max(0, value === null ? base : base - 5));
  };

  return (
    <View
      style={[styles.wrap, disabled && styles.disabled]}
      accessible
      accessibilityRole="adjustable"
      accessibilityLabel={label}
      accessibilityState={{ disabled }}
      accessibilityValue={shown === null ? { text: 'nog niet ingevuld' } : { min: 0, max: 100, now: shown }}
      accessibilityActions={[{ name: 'increment' }, { name: 'decrement' }]}
      onAccessibilityAction={onAction}
    >
      <View style={styles.top}>
        <AppText variant="listTitle">{label}</AppText>
        <View style={[styles.value, { backgroundColor: shown === null ? colors.surfaceMuted : colors.tintRed }]}>
          <AppText variant="dateDay" color={shown === null ? colors.textSecondary : colors.accentText}>
            {shown === null ? '–' : String(shown)}
          </AppText>
        </View>
      </View>
      <Slider
        style={styles.slider}
        minimumValue={0}
        maximumValue={100}
        step={1}
        value={value ?? 50}
        disabled={disabled}
        tapToSeek
        minimumTrackTintColor={shown === null ? colors.border : brand.red}
        maximumTrackTintColor={colors.border}
        thumbTintColor={shown === null ? colors.textTertiary : brand.red}
        accessibilityElementsHidden
        importantForAccessibility="no-hide-descendants"
        onSlidingStart={() => onSlidingStart?.()}
        onValueChange={(v) => setDragging(Math.round(v))}
        onSlidingComplete={(v) => {
          setDragging(null);
          onChange(Math.round(v));
          onSlidingEnd?.();
          void Haptics.selectionAsync().catch(() => undefined);
        }}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: 6 },
  disabled: { opacity: 0.5 },
  top: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  value: { minWidth: 52, alignItems: 'center', paddingHorizontal: 10, paddingVertical: 2, borderRadius: radius.sm },
  slider: { height: 40, marginHorizontal: -4 },
});
