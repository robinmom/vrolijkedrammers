import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { SaleStepperRow } from '../../features/SaleParts';
import { formatDay, formatEuro, useSaleCatalog } from '../../features/sales';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, LargeTitleHeader, QueryState, Screen } from '../../ui';

/** Dagkaarten of kaarten voor een activiteit (fase 19b): aantal kiezen; is het vol, dan de wachtlijst. */
export default function BestellenScreen() {
  const { colors } = useTheme();
  const { product: productId } = useLocalSearchParams<{ product: string }>();
  const catalog = useSaleCatalog();
  const product = catalog.data?.products.find((p) => p.id === productId);
  const [quantity, setQuantity] = useState(1);
  const max = Math.max(1, Math.min(product?.maxPerOrder ?? 10, product?.remaining ?? Number.MAX_SAFE_INTEGER));

  return (
    <Screen>
      <BackLink label="Kaarten" />
      <LargeTitleHeader title={product?.name ?? 'Kaarten'} />
      <View style={styles.content}>
        {!product ? (
          <QueryState query={catalog} />
        ) : (
          <>
            <Card style={styles.card}>
              {product.date ? <AppText variant="cardTitle">{formatDay(product.date)}</AppText> : null}
              {product.description ? (
                <AppText variant="body" color={colors.textSecondary}>
                  {product.description}
                </AppText>
              ) : null}
              {product.kind === 'DayTicket' ? (
                <AppText variant="caption" color={colors.textSecondary}>
                  Voor gasten. Leden hebben hun ledenticket onder QR code.
                </AppText>
              ) : null}
              <SaleStepperRow
                label="Aantal kaarten"
                hint={`${formatEuro(product.priceCents)} per kaart${product.remaining !== null && !product.soldOut ? ` · nog ${product.remaining} vrij` : ''}`}
                value={quantity}
                min={1}
                max={product.soldOut ? product.maxPerOrder : max}
                onChange={setQuantity}
              />
            </Card>
            <View style={styles.total}>
              <AppText variant="bodyStrong">Totaal</AppText>
              <AppText variant="sectionHeader">{formatEuro(quantity * product.priceCents)}</AppText>
            </View>
            {product.soldOut ? (
              <>
                <AppText variant="body" color={colors.accentText}>
                  Vol. Zet je op de wachtlijst: komt er plek, dan krijg je een betaallink die 48 uur geldig is.
                </AppText>
                <Button
                  label="Op de wachtlijst"
                  onPress={() => router.push(`/kaarten/afrekenen?product=${product.id}&paid=${quantity}&wachtlijst=1`)}
                />
              </>
            ) : (
              <Button label="Verder naar afrekenen" onPress={() => router.push(`/kaarten/afrekenen?product=${product.id}&paid=${quantity}`)} />
            )}
          </>
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 14, paddingBottom: 24 },
  card: { padding: 16, gap: 10 },
  total: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
});
