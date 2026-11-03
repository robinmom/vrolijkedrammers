import { radius } from '@drammers/design-tokens';
import { StyleSheet, TextInput, View, type TextInputProps } from 'react-native';
import { useText } from '../content/texts';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';

interface TextFieldProps extends Omit<TextInputProps, 'style'> {
  label: string;
  hint?: string;
}

/** Invoerveld met label boven het veld en optionele hint (formulieren, fase 9). */
export function TextField({ label, hint, placeholder, ...input }: TextFieldProps) {
  const { colors } = useTheme();
  // De tekst in het veld gaat niet door AppText, dus die vervangen we hier zelf (content/texts.tsx).
  const text = useText();
  return (
    <View style={styles.field}>
      <AppText variant="bodyStrong">{label}</AppText>
      <TextInput
        accessibilityLabel={label}
        accessibilityHint={hint}
        placeholder={placeholder === undefined ? undefined : text(placeholder)}
        placeholderTextColor={colors.textTertiary}
        style={[
          styles.input,
          { backgroundColor: colors.surface, borderColor: colors.border, color: colors.textPrimary },
        ]}
        {...input}
      />
      {hint ? (
        <AppText variant="caption" color={colors.textSecondary}>
          {hint}
        </AppText>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  field: { gap: 6 },
  input: {
    borderWidth: 1,
    borderRadius: radius.sm,
    paddingHorizontal: 14,
    minHeight: 48,
    fontFamily: 'Inter_400Regular',
    fontSize: 16,
  },
});
