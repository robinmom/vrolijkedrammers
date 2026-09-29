import { brand } from '@drammers/design-tokens';
import { router } from 'expo-router';
import { useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { SaleStepperRow } from '../../features/SaleParts';
import { formatDay, formatEuro, useSaleCatalog, type SaleProduct } from '../../features/sales';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, EmptyState, LargeTitleHeader, QueryState, Screen } from '../../ui';

/**
 * Pronkzitting bestellen (fase 19b, Figma "iOS / 2" en "3 Vrijdag vol"): een avond kiezen, gratis groepskaarten voor
 * leden (tot het aantal actieve leden van de groep, beide avonden samen; geen reservering) en losse kaarten voor
 * niet-leden. Is de avond vol, dan kiest de koper de andere avond of de wachtlijst.
 */
export default function PronkzittingScreen() {
  const { colors } = useTheme();
  const catalog = useSaleCatalog();
  const evenings = (catalog.data?.products ?? []).filter((p) => p.kind === 'Pronkzitting');
  const group = catalog.data?.group ?? null;
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [member, setMember] = useState(0);
  const [paid, setPaid] = useState(0);
  // Standaard de eerste avond met plaats.
  const selected = evenings.find((e) => e.id === selectedId) ?? evenings.find((e) => !e.soldOut) ?? evenings[0] ?? null;

  const total = member + paid;
  const fits = (e: SaleProduct) => e.remaining === null || e.remaining >= total;

  function next() {
    if (!selected || total === 0) return;
    const go = (productId: string, waitlist = false) =>
      router.push(`/kaarten/afrekenen?product=${productId}&member=${member}&paid=${paid}${waitlist ? '&wachtlijst=1' : ''}`);
    if (fits(selected)) {
      go(selected.id);
      return;
    }
    const other = evenings.find((e) => e.id !== selected.id && fits(e));
    const day = formatDay(selected.date);
    Alert.alert(
      `${day.charAt(0).toUpperCase()}${day.slice(1)} is vol`,
      other
        ? `Kies ${formatDay(other.date)} (nog ${other.remaining ?? 'genoeg'} plaatsen) of zet je op de wachtlijst. Komt er plek, dan krijg je een melding en een betaallink die 48 uur geldig is.`
        : 'Zet je op de wachtlijst. Komt er plek, dan krijg je een melding en een betaallink die 48 uur geldig is.',
      [
        ...(other ? [{ text: `${formatDay(other.date)} kiezen`, onPress: () => setSelectedId(other.id) }] : []),
        { text: 'Op de wachtlijst', onPress: () => go(selected.id, true) },
        { text: 'Annuleren', style: 'cancel' as const },
      ],
    );
  }

  return (
    <Screen>
      <BackLink label="Kaarten" />
      <LargeTitleHeader title="Pronkzitting" />
      <View style={styles.content}>
        <AppText variant="body" color={colors.textSecondary}>
          Kies een avond. Leden bestellen voor hun groep; de kaarten zitten in de contributie.
        </AppText>
        {!catalog.data ? (
          <QueryState query={catalog} />
        ) : evenings.length === 0 ? (
          <EmptyState title="Nog niet te koop" message="De kaartverkoop voor de pronkzitting is nog niet open." />
        ) : (
          <>
            <View style={styles.evenings} accessibilityRole="radiogroup">
              {evenings.map((e) => {
                const active = e.id === selected?.id;
                return (
                  <Pressable
                    key={e.id}
                    onPress={() => setSelectedId(e.id)}
                    accessibilityRole="radio"
                    accessibilityState={{ checked: active }}
                    accessibilityLabel={`${formatDay(e.date)}, ${e.soldOut ? 'vol' : `${e.remaining ?? 'genoeg'} vrij`}`}
                  >
                    <Card style={[styles.evening, active && { borderColor: brand.red, borderWidth: 2 }]}>
                      <View style={[styles.radio, { borderColor: active ? brand.red : colors.border }]}>
                        {active ? <View style={styles.radioDot} /> : null}
                      </View>
                      <View style={styles.full}>
                        <AppText variant="cardTitle">{formatDay(e.date) || e.name}</AppText>
                        {e.description ? (
                          <AppText variant="caption" color={colors.textSecondary}>
                            {e.description}
                          </AppText>
                        ) : null}
                      </View>
                      <AppText variant="label" color={e.soldOut ? colors.accentText : colors.successText}>
                        {e.soldOut ? 'Vol' : e.remaining !== null ? `${e.remaining} vrij` : ''}
                      </AppText>
                    </Card>
                  </Pressable>
                );
              })}
            </View>

            {group ? (
              <Card style={styles.card}>
                <View style={styles.cardHead}>
                  <AppText variant="cardTitle">Voor de {group.groupName}</AppText>
                  <AppText variant="label" color={colors.successText}>
                    Leden · gratis
                  </AppText>
                </View>
                <AppText variant="caption" color={colors.textSecondary}>
                  {group.activeMembers} leden · {group.ordered} al besteld door de groep. Plaatsen worden niet vastgehouden:
                  is de avond vol, dan kun je op de wachtlijst.
                </AppText>
                <SaleStepperRow
                  label="Kaarten groep"
                  hint={`max ${group.remaining}`}
                  value={member}
                  max={group.remaining}
                  onChange={setMember}
                />
              </Card>
            ) : (
              <Card style={styles.card}>
                <AppText variant="body" color={colors.textSecondary}>
                  Ben je lid? Log in, dan bestel je gratis kaarten voor je groep.
                </AppText>
                <Button label="Inloggen" variant="secondary" onPress={() => router.push('/meer/inloggen')} />
              </Card>
            )}

            <Card style={styles.card}>
              <AppText variant="cardTitle">Losse kaarten (niet-leden)</AppText>
              <SaleStepperRow
                label="Losse kaarten"
                hint={`${formatEuro(selected?.priceCents ?? 0)} per kaart`}
                value={paid}
                max={selected?.maxPerOrder ?? 10}
                onChange={setPaid}
              />
            </Card>

            <View style={styles.total}>
              <AppText variant="bodyStrong">
                Totaal: {total} {total === 1 ? 'kaart' : 'kaarten'}
              </AppText>
              <AppText variant="sectionHeader">{formatEuro(paid * (selected?.priceCents ?? 0))}</AppText>
            </View>
            <Button label="Verder naar afrekenen" onPress={next} disabled={total === 0 || !selected} />
          </>
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 14, paddingBottom: 24 },
  evenings: { gap: 10 },
  evening: { padding: 14, flexDirection: 'row', alignItems: 'center', gap: 12, borderWidth: 1, borderColor: 'transparent' },
  radio: { width: 22, height: 22, borderRadius: 11, borderWidth: 2, alignItems: 'center', justifyContent: 'center' },
  radioDot: { width: 10, height: 10, borderRadius: 5, backgroundColor: brand.red },
  full: { flex: 1 },
  card: { padding: 16, gap: 10 },
  cardHead: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: 8 },
  total: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
});
