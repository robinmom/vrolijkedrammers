import { brand, radius } from '@drammers/design-tokens';
import { FlatList, Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText, Card } from '../ui';

export type JuryColumnItem = {
  /** Uniek per rij: de registratie van een inzending of de entryId van een dans. */
  key: string;
  /** Start- of dansnummer in het donkere blokje. */
  number: string;
  title: string;
  subtitle?: string | null;
  /** Alles ingevuld voor wat er nu beoordeeld wordt. */
  done: boolean;
  /** Het lopende totaal, als er al iets is ingevuld. */
  score?: string | null;
  /** Categorie; staat er meer dan één in de lijst, dan komen er kopjes boven de groepen. */
  group?: string | null;
};

type Row = { kind: 'kop'; key: string; title: string } | { kind: 'rij'; key: string; item: JuryColumnItem };

/**
 * Kopjes per categorie, maar alleen als er iets te groeperen valt: bij één categorie zou een kopje boven de hele
 * lijst alleen ruimte kosten. De volgorde van de inzendingen blijft zoals hij binnenkomt (startvolgorde).
 */
function toRows(items: JuryColumnItem[]): Row[] {
  const groups = [...new Set(items.map((i) => i.group).filter((g): g is string => !!g))];
  if (groups.length < 2) {
    return items.map((item) => ({ kind: 'rij', key: item.key, item }));
  }
  return groups.flatMap((group) => [
    { kind: 'kop' as const, key: `kop:${group}`, title: group },
    ...items.filter((i) => i.group === group).map((item) => ({ kind: 'rij' as const, key: item.key, item })),
  ]);
}

/**
 * De lijstkolom links van het jureren op een tablet (Figma 551:981 voor de optocht, 551:863 voor het festival). Op een
 * telefoon is er geen ruimte en wordt deze kolom niet getoond; daar blijf je swipen. Zie runbook tablet.
 *
 * De kolom is een snelkoppeling, geen vervanging: swipen blijft ook op een tablet werken, en beide sturen dezelfde
 * index aan. Daardoor hoefde de pager niet te veranderen.
 */
export function JuryColumn({
  title,
  meta,
  items,
  activeKey,
  onSelect,
  width,
}: {
  title: string;
  meta: string;
  items: JuryColumnItem[];
  activeKey: string | null;
  onSelect: (key: string) => void;
  width: number;
}) {
  const { colors } = useTheme();

  return (
    <Card style={[styles.column, { width }]}>
      <View style={styles.head}>
        <AppText variant="cardTitle" accessibilityRole="header" numberOfLines={1}>
          {title}
        </AppText>
        <AppText variant="caption" color={colors.textSecondary}>
          {meta}
        </AppText>
      </View>
      <FlatList
        style={styles.flex}
        data={toRows(items)}
        keyExtractor={(row) => row.key}
        contentContainerStyle={styles.list}
        showsVerticalScrollIndicator={false}
        renderItem={({ item: row }) => {
          if (row.kind === 'kop') {
            return (
              <AppText variant="overline" color={colors.textSecondary} style={styles.kop} accessibilityRole="header">
                {row.title}
              </AppText>
            );
          }
          const item = row.item;
          const active = item.key === activeKey;
          return (
            <Pressable
              onPress={() => onSelect(item.key)}
              accessibilityRole="button"
              accessibilityState={{ selected: active }}
              accessibilityLabel={`${item.number}, ${item.title}${item.done ? `, beoordeeld${item.score ? `, ${item.score}` : ''}` : ', nog niet beoordeeld'}`}
              style={[styles.row, active && { backgroundColor: colors.tintBlue }]}
            >
              <View style={[styles.number, { backgroundColor: active ? brand.navy : colors.surfaceMuted }]}>
                <AppText variant="listTitle" color={active ? '#FFFFFF' : colors.textSecondary}>
                  {item.number}
                </AppText>
              </View>
              <View style={styles.body}>
                <AppText variant="listTitle" numberOfLines={1}>
                  {item.title}
                </AppText>
                {item.subtitle ? (
                  <AppText variant="caption" color={colors.textSecondary} numberOfLines={1}>
                    {item.subtitle}
                  </AppText>
                ) : null}
              </View>
              {/* Waar je nu staat krijgt een stip, zodat de actieve rij ook zonder kleur te zien is. */}
              {active ? (
                <View style={[styles.dot, { backgroundColor: brand.red }]} />
              ) : item.done ? (
                <AppText variant="link" color={colors.successText}>
                  ✓{item.score ? ` ${item.score}` : ''}
                </AppText>
              ) : (
                <AppText variant="link" color={colors.textTertiary}>
                  –
                </AppText>
              )}
            </Pressable>
          );
        }}
      />
    </Card>
  );
}

const styles = StyleSheet.create({
  column: { padding: 16, gap: 4, borderRadius: radius.md },
  flex: { flex: 1 },
  head: { gap: 2, paddingBottom: 8 },
  list: { gap: 4, paddingBottom: 8 },
  kop: { paddingTop: 10, paddingBottom: 2 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, borderRadius: radius.sm, padding: 8, minHeight: 56 },
  number: { width: 36, height: 36, borderRadius: radius.sm, alignItems: 'center', justifyContent: 'center' },
  body: { flex: 1, gap: 1 },
  dot: { width: 10, height: 10, borderRadius: 5 },
});
