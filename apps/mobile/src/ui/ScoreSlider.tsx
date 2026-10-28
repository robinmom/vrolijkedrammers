import { brand, radius } from '@drammers/design-tokens';
import Slider from '@react-native-community/slider';
import * as Haptics from 'expo-haptics';
import { useState } from 'react';
import { StyleSheet, View, type AccessibilityActionEvent } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';

/**
 * Slider van 0 tot en met 100 (of `max`, bijv. 10 op het Dansgardefestival) voor het jureren (fase 22b, Figma J2). De native slider van iOS en Android
 * (@react-native-community/slider): die vangt het slepen zelf af, en tijdens het slepen zet het jureerscherm het swipen
 * naar de volgende wagen uit (`onSlidingStart`/`onSlidingEnd`). Zolang er nog niets is ingevuld staat er "–" en is de
 * balk grijs. Met VoiceOver/TalkBack is het geheel één instelbaar element: omhoog of omlaag vegen = 5 erbij of eraf.
 *
 * `suggested` is een overgenomen waarde uit een eerder voorbijtrekken: die staat er wel (getal en duim), maar de balk
 * blijft grijs en `onChange` is niet gebeurd. Eén tik of veeg bevestigt hem; zo blijft "nog niet ingevuld" eerlijk.
 */
export function ScoreSlider({
  label,
  value,
  onChange,
  onSlidingStart,
  onSlidingEnd,
  disabled = false,
  max = 100,
  suggested = null,
  suggestedHint,
}: {
  label: string;
  value: number | null;
  onChange: (value: number) => void;
  onSlidingStart?: () => void;
  onSlidingEnd?: () => void;
  disabled?: boolean;
  max?: number;
  suggested?: number | null;
  suggestedHint?: string;
}) {
  const { colors } = useTheme();
  const [dragging, setDragging] = useState<number | null>(null);
  // Ingevuld of op dit moment aan het slepen: dan pas rood. Een overgenomen waarde laat de balk grijs.
  const filled = dragging !== null || value !== null;
  const shown = dragging ?? value ?? suggested;

  const onAction = (event: AccessibilityActionEvent) => {
    const step = max <= 10 ? 1 : 5;
    // Zonder eigen waarde bevestigt de eerste veeg wat er staat (overgenomen waarde of het midden).
    const base = value ?? suggested ?? Math.round(max / 2);
    if (event.nativeEvent.actionName === 'increment') onChange(Math.min(max, value === null ? base : base + step));
    if (event.nativeEvent.actionName === 'decrement') onChange(Math.max(0, value === null ? base : base - step));
  };

  return (
    <View
      style={[styles.wrap, disabled && styles.disabled]}
      accessible
      accessibilityRole="adjustable"
      accessibilityLabel={label}
      accessibilityState={{ disabled }}
      accessibilityValue={
        filled && shown !== null
          ? { min: 0, max, now: shown }
          : { text: suggested !== null ? (suggestedHint ?? `${suggested}, nog niet bevestigd`) : 'nog niet ingevuld' }
      }
      accessibilityActions={[{ name: 'increment' }, { name: 'decrement' }]}
      onAccessibilityAction={onAction}
    >
      <View style={styles.top}>
        <AppText variant="listTitle">{label}</AppText>
        <View style={[styles.value, { backgroundColor: filled ? colors.tintRed : colors.surfaceMuted }]}>
          <AppText variant="dateDay" color={filled ? colors.accentText : colors.textSecondary}>
            {shown === null ? '–' : String(shown)}
          </AppText>
        </View>
      </View>
      <Slider
        style={styles.slider}
        minimumValue={0}
        maximumValue={max}
        step={1}
        value={value ?? suggested ?? Math.round(max / 2)}
        disabled={disabled}
        tapToSeek
        minimumTrackTintColor={filled ? brand.red : colors.border}
        maximumTrackTintColor={colors.border}
        thumbTintColor={filled ? brand.red : colors.textTertiary}
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
