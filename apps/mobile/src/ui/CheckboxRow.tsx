import { brand, radius } from '@drammers/design-tokens';
import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';

/** Aankruisvak met tekst (toestemmingen in formulieren, fase 9b). De hele regel is aan te tikken. */
export function CheckboxRow({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  const { colors } = useTheme();
  return (
    <Pressable
      onPress={() => onChange(!checked)}
      accessibilityRole="checkbox"
      accessibilityState={{ checked }}
      accessibilityLabel={label}
      style={styles.row}
      hitSlop={4}
    >
      <View
        style={[
          styles.box,
          { borderColor: checked ? brand.blue : colors.border, backgroundColor: checked ? brand.blue : colors.surface },
        ]}
      >
        {checked ? (
          <AppText variant="bodyStrong" color="#FFFFFF" style={styles.check}>
            ✓
          </AppText>
        ) : null}
      </View>
      <AppText variant="caption" style={styles.label}>
        {label}
      </AppText>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', gap: 12, alignItems: 'flex-start', paddingVertical: 4 },
  box: {
    width: 26,
    height: 26,
    borderRadius: radius.sm / 2,
    borderWidth: 2,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 1,
  },
  check: { lineHeight: 18 },
  label: { flex: 1, lineHeight: 19 },
});
