import { radius } from '@drammers/design-tokens';
import { StyleSheet, TextInput, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';

/** Zoekveld dat onder de paginatitel verschijnt na een tik op het zoekicoon (Figma 02/03). */
export function SearchField({ value, onChangeText, placeholder }: { value: string; onChangeText: (text: string) => void; placeholder: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.wrap}>
      <TextInput
        value={value}
        onChangeText={onChangeText}
        placeholder={placeholder}
        placeholderTextColor={colors.textTertiary}
        autoFocus
        accessibilityLabel={placeholder}
        returnKeyType="search"
        clearButtonMode="while-editing"
        style={[styles.input, { backgroundColor: colors.surface, borderColor: colors.border, color: colors.textPrimary }]}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { paddingHorizontal: 20, paddingBottom: 12 },
  input: { borderWidth: 1, borderRadius: radius.sm, paddingHorizontal: 14, minHeight: 44, fontFamily: 'Inter_400Regular', fontSize: 15 },
});
