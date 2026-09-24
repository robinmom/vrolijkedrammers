import { fontFamily, typography } from '@drammers/design-tokens';
import { Text, type TextProps } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';

export type TextVariant = keyof typeof typography;

const families: Record<string, string> = {
  'display.semibold': fontFamily.display.semibold,
  'display.bold': fontFamily.display.bold,
  'body.regular': fontFamily.body.regular,
  'body.medium': fontFamily.body.medium,
  'body.semibold': fontFamily.body.semibold,
};

/** Titels schalen minder mee dan lopende tekst, zodat grote lettergroottes bruikbaar blijven (docs/17 §3). */
const displayVariants: TextVariant[] = ['largeTitle', 'heroTitle', 'countdown', 'cardTitle', 'dateDay'];

interface AppTextProps extends TextProps {
  variant?: TextVariant;
  color?: string;
}

export function AppText({ variant = 'body', color, style, ...rest }: AppTextProps) {
  const { colors } = useTheme();
  const token: { font: string; size: number; lineHeight?: number; uppercase?: boolean } = typography[variant];
  return (
    <Text
      maxFontSizeMultiplier={displayVariants.includes(variant) ? 1.6 : 2}
      style={[
        {
          fontFamily: families[token.font],
          fontSize: token.size,
          lineHeight: token.lineHeight,
          textTransform: token.uppercase ? 'uppercase' : undefined,
          color: color ?? colors.textPrimary,
        },
        style,
      ]}
      {...rest}
    />
  );
}
