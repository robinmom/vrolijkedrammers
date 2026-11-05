import { Children, type ReactNode } from 'react';
import { StyleSheet, View, type ViewStyle } from 'react-native';
import { useIsWide } from './layout';

/**
 * Twee kolommen naast elkaar zodra het scherm breed genoeg is, en anders gewoon onder elkaar. Voor schermen die uit
 * kaarten bestaan: op een iPad in landschap staat zo'n lijst anders in een smalle band met veel leegte ernaast.
 *
 * De verdeling is vast en niet "zoveel mogelijk per rij": zo blijft de leesvolgorde links-boven naar rechts-onder,
 * en blijft de telefoonindeling precies zoals hij was.
 */
export function Columns({ left, right, gap = 16, style }: { left: ReactNode; right: ReactNode; gap?: number; style?: ViewStyle }) {
  const wide = useIsWide();
  // Op een telefoon geen extra laag eromheen: dan staat de indeling er precies zoals hij stond, tot de afstanden aan toe.
  if (!wide) {
    return (
      <>
        {left}
        {right}
      </>
    );
  }
  return (
    <View testID="columns-row" style={[styles.row, { gap }, style]}>
      <View style={styles.column}>{left}</View>
      <View style={styles.column}>{right}</View>
    </View>
  );
}

/**
 * Een lijst kaarten over twee kolommen op een breed scherm, op volgorde: de eerste helft links, de tweede rechts.
 * Op een telefoon blijft het één kolom.
 */
export function CardColumns({ children, gap = 12 }: { children: ReactNode; gap?: number }) {
  const wide = useIsWide();
  const items = Children.toArray(children);
  if (!wide || items.length < 2) {
    return <>{items}</>;
  }
  const half = Math.ceil(items.length / 2);
  return (
    <View testID="columns-row" style={[styles.row, { gap }]}>
      <View style={[styles.column, { gap }]}>{items.slice(0, half)}</View>
      <View style={[styles.column, { gap }]}>{items.slice(half)}</View>
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'flex-start' },
  // Even breed, en smal mogen worden: zonder minWidth 0 duwt een lange titel de kolom uit elkaar.
  column: { flex: 1, minWidth: 0 },
});
