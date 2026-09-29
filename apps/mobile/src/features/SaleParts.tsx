import { brand } from '@drammers/design-tokens';
import { router } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import QRCode from 'react-native-qrcode-svg';
import { useTheme } from '../theme/ThemeProvider';
import { AppText, Button, Card } from '../ui';
import { formatDay, type OrderView } from './sales';

/** Aantal kiezen met − en + (Figma Kaartverkoop); toegankelijk als "adjustable". */
export function Stepper({
  label,
  value,
  min = 0,
  max,
  onChange,
}: {
  label: string;
  value: number;
  min?: number;
  max: number;
  onChange: (value: number) => void;
}) {
  const { colors } = useTheme();
  const set = (next: number) => onChange(Math.max(min, Math.min(max, next)));
  return (
    <View
      style={styles.stepper}
      accessible
      accessibilityRole="adjustable"
      accessibilityLabel={label}
      accessibilityValue={{ min, max, now: value }}
      accessibilityActions={[{ name: 'increment' }, { name: 'decrement' }]}
      onAccessibilityAction={(e) => set(value + (e.nativeEvent.actionName === 'increment' ? 1 : -1))}
    >
      <Pressable onPress={() => set(value - 1)} disabled={value <= min} hitSlop={8} testID={`${label}-min`}>
        <AppText variant="sectionHeader" color={value <= min ? colors.textTertiary : brand.blue}>
          −
        </AppText>
      </Pressable>
      <AppText variant="sectionHeader" style={styles.value}>
        {value}
      </AppText>
      <Pressable onPress={() => set(value + 1)} disabled={value >= max} hitSlop={8} testID={`${label}-plus`}>
        <AppText variant="sectionHeader" color={value >= max ? colors.textTertiary : brand.blue}>
          +
        </AppText>
      </Pressable>
    </View>
  );
}

/** Regel met een label, toelichting en de teller rechts (Figma: "Kaarten groep · max 5  − 5 +"). */
export function SaleStepperRow({
  label,
  hint,
  value,
  min = 0,
  max,
  onChange,
}: {
  label: string;
  hint?: string;
  value: number;
  min?: number;
  max: number;
  onChange: (value: number) => void;
}) {
  const { colors } = useTheme();
  return (
    <View style={styles.stepperRow}>
      <View style={styles.full}>
        <AppText variant="body">{label}</AppText>
        {hint ? (
          <AppText variant="caption" color={colors.textSecondary}>
            {hint}
          </AppText>
        ) : null}
      </View>
      <Stepper label={label} value={value} min={min} max={max} onChange={onChange} />
    </View>
  );
}

/**
 * Een bestelling in Mijn kaarten (Figma scherm 5): één QR voor alle kaarten. Bij het scannen gaan alle personen
 * tegelijk naar binnen; kaarten die met een groepslid zijn gedeeld staan niet meer in deze QR.
 */
export function OrderCard({ order }: { order: OrderView }) {
  const { colors } = useTheme();
  const persons = (n: number) => `${n} ${n === 1 ? 'persoon' : 'personen'}`;
  const active = order.tickets.filter((t) => t.status === 'Active');
  const used = order.tickets.some((t) => t.status === 'Used');
  const total = order.tickets.reduce((n, t) => n + t.quantity, 0);
  const shared = order.sharedWith.reduce((n, s) => n + s.quantity, 0);
  return (
    <Card style={styles.card}>
      <View style={styles.head}>
        <AppText variant="label" color={brand.yellow}>
          {[order.productName, formatDay(order.date)].filter(Boolean).join(' · ').toUpperCase()}
        </AppText>
        <AppText variant="sectionHeader" color="#FFFFFF">
          {order.kind === 'Tokens' ? `${total} munten` : `${persons(total)}${order.groupName ? ` · ${order.groupName}` : ''}`}
        </AppText>
        <AppText variant="caption" color="rgba(255,255,255,0.85)">
          Bestelling {order.number} ·{' '}
          {order.status === 'AwaitingPayment' ? 'nog niet betaald' : order.sharedBy ? `gedeeld door ${order.sharedBy}` : 'betaald'}
        </AppText>
      </View>
      <View style={styles.zone}>
        {order.status === 'AwaitingPayment' ? (
          <AppText variant="body" color={colors.textSecondary} style={styles.center}>
            De QR verschijnt zodra de betaling binnen is.
          </AppText>
        ) : order.kind === 'Tokens' ? (
          <>
            <AppText variant="body" color={colors.textSecondary} style={styles.center}>
              {active.length > 0 ? 'Haal je munten op bij de kassa met de munten-QR.' : 'Afgehaald.'}
            </AppText>
            {active.length > 0 ? <Button label="Munten-QR tonen" onPress={() => router.push('/munten')} /> : null}
          </>
        ) : (
          active.map((t) => (
            <View key={t.id} style={styles.ticket}>
              {t.code ? (
                <View accessible accessibilityLabel={`QR-code voor ${persons(t.quantity)}. Laat deze scannen bij de ingang.`}>
                  <QRCode value={t.code} size={220} ecl="M" color={brand.navy} backgroundColor="#FFFFFF" quietZone={0} />
                </View>
              ) : null}
              <AppText variant="bodyStrong" style={styles.center}>
                Geldig voor {persons(t.quantity)}
                {shared > 0 ? ` · ${order.sharedWith.map((s) => `${s.quantity} gedeeld met ${s.name}`).join(', ')}` : ''}
              </AppText>
              {t.canShare ? (
                <Button
                  label="Kaart delen met groepslid"
                  variant="secondary"
                  icon="delen"
                  onPress={() => router.push(`/kaarten/delen?ticket=${t.id}&order=${order.id}`)}
                />
              ) : null}
            </View>
          ))
        )}
        {used && active.length === 0 && order.kind !== 'Tokens' ? (
          <AppText variant="body" color={colors.textSecondary} style={styles.center}>
            Deze QR is al gescand.
          </AppText>
        ) : null}
      </View>
    </Card>
  );
}

const styles = StyleSheet.create({
  stepper: { flexDirection: 'row', alignItems: 'center', gap: 16 },
  value: { minWidth: 28, textAlign: 'center' },
  card: { padding: 0, overflow: 'hidden', borderRadius: 20 },
  head: { backgroundColor: brand.navy, paddingHorizontal: 20, paddingVertical: 16, gap: 4 },
  zone: { backgroundColor: '#FFFFFF', padding: 20, alignItems: 'center', gap: 12 },
  ticket: { alignItems: 'center', gap: 12, alignSelf: 'stretch' },
  center: { textAlign: 'center' },
  stepperRow: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  full: { flex: 1 },
});
