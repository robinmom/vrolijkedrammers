import { brand, radius } from '@drammers/design-tokens';
import { Pressable, ScrollView, StyleSheet } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';

interface FilterChipsProps<T extends string> {
  options: { value: T; label: string }[];
  selected: T;
  onChange: (value: T) => void;
  accessibilityLabel: string;
}

/** Filterchips (Figma 02 Programma): actief = rood gevuld, inactief = wit met rand. */
export function FilterChips<T extends string>({ options, selected, onChange, accessibilityLabel }: FilterChipsProps<T>) {
  const { colors, mode } = useTheme();
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.row} accessibilityLabel={accessibilityLabel}>
      {options.map((option) => {
        const active = option.value === selected;
        return (
          <Pressable
            key={option.value}
            onPress={() => onChange(option.value)}
            accessibilityRole="button"
            accessibilityState={{ selected: active }}
            style={[
              styles.chip,
              active
                ? { backgroundColor: brand.red, borderColor: brand.red }
                : { backgroundColor: colors.surface, borderColor: mode === 'dark' ? colors.border : 'rgba(18,48,71,0.15)' },
            ]}
          >
            <AppText variant="link" style={styles.text} color={active ? '#FFFFFF' : colors.textPrimary}>
              {option.label}
            </AppText>
          </Pressable>
        );
      })}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  row: { gap: 8, paddingHorizontal: 20 },
  chip: { borderRadius: radius.pill, borderWidth: 1, paddingHorizontal: 14, paddingVertical: 8, minHeight: 36, justifyContent: 'center' },
  text: { fontSize: 13 },
});
