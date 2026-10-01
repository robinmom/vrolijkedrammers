import { brand, radius } from '@drammers/design-tokens';
import * as Haptics from 'expo-haptics';
import { useMemo, useState } from 'react';
import { PanResponder, StyleSheet, View, type AccessibilityActionEvent, type GestureResponderEvent, type LayoutChangeEvent } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';

const THUMB = 28;

/**
 * Slider van 0 tot en met 100 voor het jureren (fase 22b, Figma J2). Slepen of tikken op de balk; zolang er nog niets is
 * ingevuld staat er "–". Met VoiceOver/TalkBack is het een instelbaar element: omhoog of omlaag vegen = 5 erbij of eraf.
 * Het slepen houdt de vinger vast, zodat het swipen naar de volgende wagen niet per ongeluk start.
 */
export function ScoreSlider({
  label,
  value,
  onChange,
  disabled = false,
}: {
  label: string;
  value: number | null;
  onChange: (value: number) => void;
  disabled?: boolean;
}) {
  const { colors } = useTheme();
  const [width, setWidth] = useState(0);
  const [dragging, setDragging] = useState<number | null>(null);
  const shown = dragging ?? value;

  // De balk zelf vangt de aanraking (de onderdelen erin niet), dus locationX is steeds de plek op de balk.
  const responder = useMemo(() => {
    const toValue = (x: number) => (width <= 0 ? 0 : Math.max(0, Math.min(100, Math.round((x / width) * 100))));
    const commit = (event: GestureResponderEvent) => {
      const v = toValue(event.nativeEvent.locationX);
      setDragging(null);
      onChange(v);
      void Haptics.selectionAsync().catch(() => undefined);
    };
    return PanResponder.create({
      onStartShouldSetPanResponder: () => !disabled,
      onStartShouldSetPanResponderCapture: () => !disabled,
      onMoveShouldSetPanResponder: () => !disabled,
      onMoveShouldSetPanResponderCapture: () => !disabled,
      onPanResponderTerminationRequest: () => false,
      onPanResponderGrant: (event) => setDragging(toValue(event.nativeEvent.locationX)),
      onPanResponderMove: (event) => setDragging(toValue(event.nativeEvent.locationX)),
      onPanResponderRelease: commit,
      onPanResponderTerminate: commit,
    });
  }, [width, disabled, onChange]);

  const onAction = (event: AccessibilityActionEvent) => {
    const base = value ?? 50;
    if (event.nativeEvent.actionName === 'increment') onChange(Math.min(100, value === null ? base : base + 5));
    if (event.nativeEvent.actionName === 'decrement') onChange(Math.max(0, value === null ? base : base - 5));
  };

  const position = shown === null ? width / 2 : (width * shown) / 100;
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
      <View style={styles.track} onLayout={(e: LayoutChangeEvent) => setWidth(e.nativeEvent.layout.width)} {...responder.panHandlers}>
        <View pointerEvents="none" style={[styles.base, { backgroundColor: colors.surfaceMuted }]} />
        {shown !== null ? <View pointerEvents="none" style={[styles.fill, { width: position, backgroundColor: brand.red }]} /> : null}
        <View
          pointerEvents="none"
          style={[
            styles.thumb,
            { left: Math.max(0, Math.min(width - THUMB, position - THUMB / 2)), borderColor: shown === null ? colors.border : brand.red },
          ]}
        />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: 6 },
  disabled: { opacity: 0.5 },
  top: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  value: { minWidth: 52, alignItems: 'center', paddingHorizontal: 10, paddingVertical: 2, borderRadius: radius.sm },
  track: { height: 36, justifyContent: 'center' },
  base: { position: 'absolute', left: 0, right: 0, height: 8, borderRadius: 4 },
  fill: { position: 'absolute', left: 0, height: 8, borderRadius: 4 },
  thumb: {
    position: 'absolute',
    width: THUMB,
    height: THUMB,
    borderRadius: THUMB / 2,
    borderWidth: 3,
    backgroundColor: '#FFFFFF',
    shadowColor: '#000000',
    shadowOpacity: 0.18,
    shadowRadius: 6,
    shadowOffset: { width: 0, height: 2 },
    elevation: 3,
  },
});
