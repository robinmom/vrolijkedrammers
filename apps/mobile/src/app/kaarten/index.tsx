import { brand } from '@drammers/design-tokens';
import { router } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { useRefresh } from '../../api/useRefresh';
import { formatDay, formatEuro, salesKeys, useSaleCatalog, type SaleProduct } from '../../features/sales';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, EmptyState, LargeTitleHeader, QueryState, Screen } from '../../ui';

/** Waar een product naartoe gaat: de pronkzitting heeft een eigen scherm (avond kiezen, groepskaarten), munten ook. */
function open(p: SaleProduct) {
  if (p.kind === 'Pronkzitting') router.push('/kaarten/pronkzitting');
  else if (p.kind === 'Tokens') router.push('/munten/kopen');
  else router.push(`/kaarten/bestellen?product=${p.id}`);
}

/**
 * Kaarten (fase 19b, Figma "iOS / 1 Kaarten"): alles wat te koop is, voor iedereen (ook zonder account), en Mijn kaarten.
 * De pronkzittingavonden staan samen onder één regel.
 */
export default function KaartenScreen() {
  const { colors } = useTheme();
  const catalog = useSaleCatalog();
  const refresh = useRefresh([salesKeys.catalog]);
  const products = catalog.data?.products ?? [];
  const evenings = products.filter((p) => p.kind === 'Pronkzitting');
  const others = products.filter((p) => p.kind !== 'Pronkzitting' && (p.kind !== 'Tokens' || catalog.data?.isMember));

  return (
    <Screen {...refresh}>
      <BackLink label="Terug" />
      <LargeTitleHeader title="Kaarten" />
      <View style={styles.content}>
        <Button label="Mijn kaarten" icon="qr" variant="secondary" onPress={() => router.push('/kaarten/mijn')} />
        {!catalog.data ? (
          <QueryState query={catalog} />
        ) : products.length === 0 ? (
          <EmptyState title="Nu niets te koop" message="Zodra de kaartverkoop opent, staat het hier." />
        ) : (
          <>
            {evenings.length > 0 ? (
              <Row
                title="Pronkzitting"
                meta={evenings.map((e) => `${formatDay(e.date)}${e.soldOut ? ' (vol)' : ''}`).join(' · ')}
                price={`${formatEuro(evenings[0]!.priceCents)} per kaart${catalog.data.isMember ? ' · leden gratis via hun groep' : ''}`}
                onPress={() => router.push('/kaarten/pronkzitting')}
              />
            ) : null}
            {others.map((p) => (
              <Row
                key={p.id}
                title={p.name}
                meta={[formatDay(p.date), p.soldOut ? 'vol' : p.remaining !== null ? `nog ${p.remaining} vrij` : null].filter(Boolean).join(' · ')}
                price={`${formatEuro(p.priceCents)} ${p.kind === 'Tokens' ? 'per munt · persoonsgebonden' : 'per kaart'}`}
                onPress={() => open(p)}
              />
            ))}
          </>
        )}
        <AppText variant="caption" color={colors.textSecondary}>
          Betalen gaat met iDEAL via Mollie. Kaarten en munten worden niet terugbetaald.
          {catalog.data?.isMember ? '' : ' Munten zijn alleen voor leden: log in om ze te kopen.'}
        </AppText>
      </View>
    </Screen>
  );
}

function Row({ title, meta, price, onPress }: { title: string; meta: string; price: string; onPress: () => void }) {
  const { colors } = useTheme();
  return (
    <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={`${title}. ${meta}. ${price}`}>
      <Card style={styles.row}>
        <View style={styles.rowText}>
          <AppText variant="cardTitle">{title}</AppText>
          {meta ? (
            <AppText variant="caption" color={colors.textSecondary}>
              {meta}
            </AppText>
          ) : null}
          <AppText variant="bodyStrong" color={brand.red}>
            {price}
          </AppText>
        </View>
        <AppText variant="sectionHeader" color={colors.textTertiary}>
          ›
        </AppText>
      </Card>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 12, paddingBottom: 24 },
  row: { padding: 16, flexDirection: 'row', alignItems: 'center', gap: 12 },
  rowText: { flex: 1, gap: 2 },
});
