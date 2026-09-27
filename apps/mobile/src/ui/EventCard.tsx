import { brand } from '@drammers/design-tokens';
import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';
import { Badge, type BadgeVariant } from './Badge';
import { Card } from './Card';
import { DateBlock } from './DateBlock';
import { Icon } from './Icon';

interface EventCardProps {
  title: string;
  date: { weekday: string; day: string; month: string };
  time?: string;
  location?: string;
  badge?: { label: string; variant: BadgeVariant };
  /** Hoogtepunt: rode rand en rood datumblok (Figma 02). */
  highlighted?: boolean;
  dateVariant?: 'solid' | 'tinted';
  /** `lines` = tijd en locatie met icoon onder elkaar (Figma 02); `inline` = één regel "11:11 uur · Dorpsplein" (Figma 01). */
  meta?: 'lines' | 'inline';
  onPress?: () => void;
  testID?: string;
}

export function EventCard({ title, date, time, location, badge, highlighted, dateVariant = 'tinted', meta = 'lines', onPress, testID }: EventCardProps) {
  const { colors } = useTheme();
  const label = [title, `${date.weekday} ${date.day} ${date.month}`, time, location].filter(Boolean).join(', ');
  return (
    <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={label} testID={testID}>
      <Card style={[styles.card, highlighted && styles.highlighted]}>
        <DateBlock {...date} variant={highlighted ? 'solid' : dateVariant} />
        <View style={[styles.body, meta === 'inline' && styles.bodyInline]}>
          <View style={styles.titleRow}>
            <AppText variant="listTitle" numberOfLines={2} style={styles.title}>
              {title}
            </AppText>
            {badge ? <Badge {...badge} /> : null}
          </View>
          {meta === 'inline' && (time || location) ? (
            <AppText variant="caption" color={colors.textSecondary} numberOfLines={1}>
              {[time, location].filter(Boolean).join(' · ')}
            </AppText>
          ) : null}
          {meta === 'lines' && time ? (
            <View style={styles.meta}>
              <Icon name="klok" size={14} color={colors.textSecondary} />
              <AppText variant="caption" color={colors.textSecondary}>
                {time}
              </AppText>
            </View>
          ) : null}
          {meta === 'lines' && location ? (
            <View style={styles.meta}>
              <Icon name="locatie" size={14} color={colors.textSecondary} />
              <AppText variant="caption" color={colors.textSecondary}>
                {location}
              </AppText>
            </View>
          ) : null}
        </View>
        <Icon name="chevron" size={20} color={colors.textTertiary} />
      </Card>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  card: { flexDirection: 'row', alignItems: 'center', gap: 14 },
  highlighted: { borderWidth: 1.5, borderColor: brand.red },
  body: { flex: 1, gap: 4 },
  bodyInline: { gap: 3 },
  titleRow: { flexDirection: 'row', alignItems: 'center', gap: 8, flexWrap: 'wrap' },
  title: { flexShrink: 1 },
  meta: { flexDirection: 'row', alignItems: 'center', gap: 6 },
});
