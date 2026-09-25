import { radius } from '@drammers/design-tokens';
import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';
import { Badge } from './Badge';
import { Button } from './Button';
import { Card } from './Card';
import { RemoteImage } from './RemoteImage';

interface NewsItemProps {
  id: string;
  title: string;
  imageUrl: string | null;
  onPress: () => void;
}

/** Uitgelicht bericht bovenaan Nieuws (Figma 03). */
export function FeaturedNewsCard({ id, title, imageUrl, summary, category, date, onPress }: NewsItemProps & { summary: string | null; category: string | null; date: string }) {
  const { colors } = useTheme();
  return (
    <Card style={styles.featured}>
      <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={`${title}, ${date}`}>
        <RemoteImage uri={imageUrl} cacheKey={`news-${id}`} style={styles.featuredImage} />
      </Pressable>
      <View style={styles.featuredBody}>
        <View style={styles.meta}>
          {category ? <Badge label={category} variant="category" /> : null}
          <AppText variant="label" color={colors.textSecondary} style={styles.date}>
            {date}
          </AppText>
        </View>
        <AppText variant="cardTitle" style={styles.featuredTitle} accessibilityRole="header">
          {title}
        </AppText>
        {summary ? (
          <AppText variant="body" color={colors.textSecondary} style={styles.summary}>
            {summary}
          </AppText>
        ) : null}
        <Button label="Lees meer" onPress={onPress} />
      </View>
    </Card>
  );
}

/**
 * Nieuwsregel. `home` (Figma 01): datum in rood boven de titel, afbeelding 72.
 * `list` (Figma 03): categorie, titel en korte datum, afbeelding 76.
 */
export function NewsRow({ id, title, imageUrl, overline, date, variant, onPress }: NewsItemProps & { overline: string | null; date?: string; variant: 'home' | 'list' }) {
  const { colors } = useTheme();
  const size = variant === 'home' ? 72 : 76;
  return (
    <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={[overline, title, date].filter(Boolean).join(', ')}>
      <Card style={styles.row}>
        <RemoteImage uri={imageUrl} cacheKey={`news-${id}`} style={{ width: size, height: size, borderRadius: radius.sm }} />
        <View style={styles.rowBody}>
          {overline ? (
            <AppText variant="overline" color={colors.accentText}>
              {overline}
            </AppText>
          ) : null}
          <AppText variant="bodyStrong" style={styles.rowTitle} numberOfLines={3}>
            {title}
          </AppText>
          {date ? (
            <AppText variant="label" color={colors.textSecondary} style={styles.date}>
              {date}
            </AppText>
          ) : null}
        </View>
      </Card>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  // Geen overflow: 'hidden' op de kaart: dat zou op iOS de schaduw afknippen; de afbeelding krijgt zelf ronde hoeken.
  featured: { padding: 0, borderRadius: radius.lg },
  featuredImage: { height: 190, width: '100%', borderTopLeftRadius: radius.lg, borderTopRightRadius: radius.lg },
  featuredBody: { padding: 16, gap: 8 },
  meta: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  date: { fontFamily: 'Inter_400Regular' },
  featuredTitle: { fontSize: 21, lineHeight: 27 },
  summary: { fontSize: 14, lineHeight: 21 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, padding: 10 },
  rowBody: { flex: 1, gap: 4 },
  rowTitle: { lineHeight: 20 },
});
