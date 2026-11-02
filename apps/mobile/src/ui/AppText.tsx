import { fontFamily, typography } from '@drammers/design-tokens';
import { Children } from 'react';
import { Text, type TextProps } from 'react-native';
import { useText } from '../content/texts';
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

/**
 * Alle zichtbare tekst in de app gaat hier langs, dus dit is ook de plek waar een in het portal aangepaste tekst in
 * de plaats komt (zie content/texts.tsx). Losse tekst tussen de tags wordt vervangen; een tekst die de app ter plekke
 * samenstelt staat als geheel nergens in de bron en blijft dus zoals hij is.
 */
export function AppText({ variant = 'body', color, style, children, ...rest }: AppTextProps) {
  const { colors } = useTheme();
  const text = useText();
  const token: { font: string; size: number; lineHeight?: number; uppercase?: boolean } = typography[variant];
  const content =
    typeof children === 'string'
      ? text(children)
      : Children.map(children, (child) => (typeof child === 'string' ? text(child) : child));
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
    >
      {content}
    </Text>
  );
}
