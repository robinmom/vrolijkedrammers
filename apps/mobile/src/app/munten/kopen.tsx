import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useSessionStatus } from '../../auth/useSession';
import { SaleStepperRow } from '../../features/SaleParts';
import { formatEuro, useSaleCatalog } from '../../features/sales';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, EmptyState, FilterChips, LargeTitleHeader, QueryState, Screen } from '../../ui';

const PRESETS = [10, 20, 40];

/**
 * Munten kopen (fase 19b, Figma "iOS / 6"): alleen voor leden en persoonsgebonden. Afhalen bij de kassa met de
 * munten-QR, pas als de betaling is afgerond.
 */
export default function MuntenKopenScreen() {
  const { colors } = useTheme();
  const status = useSessionStatus();
  const catalog = useSaleCatalog();
  const product = catalog.data?.products.find((p) => p.kind === 'Tokens');
  const [quantity, setQuantity] = useState(20);

  return (
    <Screen>
      <BackLink label="Munten" />
      <LargeTitleHeader title="Consumptiemunten" />
      <View style={styles.content}>
        <AppText variant="body" color={colors.textSecondary}>
          Alleen voor leden. Aankoop is persoonsgebonden: je haalt de munten zelf op bij de kassa met de munten-QR op het
          beginscherm. Die QR kun je niet delen en werkt alleen op dit toestel: hij is nooit over te zetten naar een
          ander toestel.
        </AppText>
        {status !== 'signedIn' ? (
          <Card style={styles.card}>
            <AppText variant="body">Log in als lid om munten te kopen.</AppText>
            <Button label="Inloggen" onPress={() => router.push('/meer/inloggen')} />
          </Card>
        ) : !catalog.data ? (
          <QueryState query={catalog} />
        ) : !product ? (
          <EmptyState title="Nu geen munten te koop" message="Zodra de verkoop opent, kun je hier munten kopen." />
        ) : (
          <>
            <Card style={styles.card}>
              <SaleStepperRow label="Aantal munten" value={quantity} min={1} max={product.maxPerOrder} onChange={setQuantity} />
              <FilterChips
                accessibilityLabel="Snel kiezen"
                options={PRESETS.map((n) => ({ value: String(n), label: `${n} munten` }))}
                selected={String(quantity)}
                onChange={(value) => setQuantity(Number(value))}
              />
            </Card>
            <View style={styles.total}>
              <AppText variant="body" color={colors.textSecondary}>
                {quantity} × {formatEuro(product.priceCents)}
              </AppText>
              <AppText variant="sectionHeader">{formatEuro(quantity * product.priceCents)}</AppText>
            </View>
            <Button
              label={`Betalen met iDEAL · ${formatEuro(quantity * product.priceCents)}`}
              onPress={() => router.push(`/kaarten/afrekenen?product=${product.id}&paid=${quantity}`)}
            />
            <AppText variant="caption" color={colors.textSecondary}>
              Afhalen kan pas als de betaling is afgerond. Munten worden niet terugbetaald.
            </AppText>
          </>
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 14, paddingBottom: 24 },
  card: { padding: 16, gap: 12 },
  total: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
});
