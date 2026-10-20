import { useMemo } from 'react';
import { Linking, StyleSheet } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText, type TextVariant } from './AppText';

/** Herkent http- en https-webadressen in lopende tekst. We matchen zo ruim mogelijk (tot de eerstvolgende spatie). */
const URL_REGEX = /https?:\/\/\S+/gi;
/** Afsluitende leestekens (punt, komma, haakje e.d.) horen niet bij de link; die knippen we er weer af. */
const TRAILING_PUNCTUATION = /[.,;:!?)\]}>'"»]+$/;

interface TextPart {
  text: string;
  /** Gezet wanneer dit deel een aantikbaar webadres is. */
  url?: string;
}

/**
 * Splitst een string in gewone tekst en webadressen. De omringende tekst en regelafbrekingen blijven behouden; een
 * afsluitende punt of komma blijft buiten de link (bijv. "… drammerskrant-2026." levert een link zonder de punt op).
 */
export function splitLinks(text: string): TextPart[] {
  const parts: TextPart[] = [];
  let last = 0;
  for (const match of text.matchAll(URL_REGEX)) {
    const start = match.index ?? 0;
    const url = match[0].replace(TRAILING_PUNCTUATION, '');
    if (start > last) parts.push({ text: text.slice(last, start) });
    parts.push({ text: url, url });
    // De eventueel afgeknipte leestekens vallen vanzelf in het volgende tekstdeel.
    last = start + url.length;
  }
  if (last < text.length) parts.push({ text: text.slice(last) });
  return parts;
}

/** Opent alleen veilige webadressen (http/https) via het systeem. */
function openUrl(url: string) {
  if (/^https?:\/\//i.test(url)) void Linking.openURL(url);
}

interface LinkTextProps {
  children: string;
  variant?: TextVariant;
  /** Kleur van de gewone tekst; de link krijgt altijd de linkkleur uit het thema. */
  color?: string;
}

/** Toont tekst waarin webadressen automatisch aantikbare links worden. */
export function LinkText({ children, variant = 'body', color }: LinkTextProps) {
  const { colors } = useTheme();
  const parts = useMemo(() => splitLinks(children), [children]);
  return (
    <AppText variant={variant} color={color}>
      {parts.map((part, index) =>
        part.url ? (
          <AppText
            key={index}
            variant={variant}
            color={colors.linkText}
            style={styles.link}
            accessibilityRole="link"
            accessibilityLabel={`Open webadres ${part.url}`}
            onPress={() => openUrl(part.url as string)}
          >
            {part.text}
          </AppText>
        ) : (
          part.text
        ),
      )}
    </AppText>
  );
}

const styles = StyleSheet.create({
  link: { textDecorationLine: 'underline' },
});
