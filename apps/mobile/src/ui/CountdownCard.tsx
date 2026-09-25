import { brand, radius } from '@drammers/design-tokens';
import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { countdown } from '../lib/dates';
import { AppText } from './AppText';

interface CountdownCardProps {
  target: Date;
  /** "carnaval 2027". */
  label: string;
  /** Tekst zodra het moment bereikt is (tot het einde van carnaval). */
  reachedText: string;
  /** Vaste "nu" voor tests en snapshots. */
  now?: Date;
}

const units = [
  { key: 'days', label: 'Dagen', spoken: ['dag', 'dagen'] },
  { key: 'hours', label: 'Uur', spoken: ['uur', 'uur'] },
  { key: 'minutes', label: 'Minuten', spoken: ['minuut', 'minuten'] },
  { key: 'seconds', label: 'Seconden', spoken: ['seconde', 'seconden'] },
] as const;

/** Rode countdown-kaart (Figma 01 Home). */
export function CountdownCard({ target, label, reachedText, now: fixedNow }: CountdownCardProps) {
  const [now, setNow] = useState(() => fixedNow ?? new Date());
  useEffect(() => {
    if (fixedNow) {
      return;
    }
    const timer = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(timer);
  }, [fixedNow]);

  const left = countdown(target, now);
  if (!left) {
    return (
      <View style={styles.card} accessible accessibilityRole="text">
        <AppText variant="cardTitle" color="#FFFFFF">
          {reachedText}
        </AppText>
      </View>
    );
  }

  // Schermlezers krijgen één zin zonder seconden, zodat de kaart niet elke seconde opnieuw wordt voorgelezen.
  const spoken = units
    .filter((u) => u.key !== 'seconds')
    .map((u) => `${left[u.key]} ${left[u.key] === 1 ? u.spoken[0] : u.spoken[1]}`)
    .join(', ');
  return (
    <View style={styles.card} accessible accessibilityRole="text" accessibilityLabel={`Nog ${spoken} tot ${label}`}>
      <AppText variant="caption" color="rgba(255,255,255,0.9)" style={styles.title}>
        Nog tot {label}
      </AppText>
      <View style={styles.row}>
        {units.map((unit) => (
          <View key={unit.key} style={styles.tile}>
            <AppText variant="countdown" color="#FFFFFF">
              {left[unit.key]}
            </AppText>
            <AppText variant="overline" color="rgba(255,255,255,0.85)" style={styles.unit}>
              {unit.label}
            </AppText>
          </View>
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: brand.red,
    borderRadius: radius.lg,
    padding: 16,
    gap: 10,
    shadowColor: 'rgba(18,48,71,0.08)',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 1,
    shadowRadius: 8,
    elevation: 3,
  },
  title: { fontFamily: 'Inter_500Medium' },
  row: { flexDirection: 'row', gap: 8 },
  tile: { flex: 1, alignItems: 'center', backgroundColor: 'rgba(255,255,255,0.14)', borderRadius: radius.sm, paddingVertical: 8, paddingHorizontal: 4 },
  unit: { fontFamily: 'Inter_400Regular', textTransform: 'none' },
});
