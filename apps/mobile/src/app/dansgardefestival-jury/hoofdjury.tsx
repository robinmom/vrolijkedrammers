import { brand, radius } from '@drammers/design-tokens';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Alert, StyleSheet, View } from 'react-native';
import { api, unwrap } from '../../api/client';
import { queryKeys, useHeadJury, useMe } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, EmptyState, FilterChips, LargeTitleHeader, QueryState, Screen } from '../../ui';

/**
 * H1–H2 Hoofdjury op het Dansgardefestival (2026-10-15, Figma "💃 Dansgardefestival"): na het publiceren door de
 * organisatie per deel van de prijsuitreiking de uitslag per categorie, van de laatste plaats naar de winnaar (zo lees je
 * hem voor). Na de laatste categorie: "Prijsuitreiking afgerond?" (Ja/Nee; de organisatie kan dit in het portal overrulen).
 */
export default function HeadJuryScreen() {
  const { colors } = useTheme();
  const me = useMe();
  const allowed = me.data?.permissions.includes('festival.head-jury') ?? false;
  const head = useHeadJury(allowed);
  const refresh = useRefresh([queryKeys.headJury]);
  const client = useQueryClient();
  const [part, setPart] = useState(0);
  const [busy, setBusy] = useState(false);
  const h = head.data;

  async function complete(done: boolean) {
    setBusy(true);
    try {
      await unwrap(api.POST('/api/v1/festival-jury/head/awards', { body: { completed: done } }));
      await client.invalidateQueries({ queryKey: queryKeys.headJury });
    } finally {
      setBusy(false);
    }
  }

  const ask = () =>
    Alert.alert('Prijsuitreiking afgerond?', 'Bij Ja mag de organisatie de uitslag op de website zetten en gaan de juryrapporten naar de verenigingen.', [
      { text: 'Nee, nog niet', style: 'cancel' },
      { text: 'Ja, afgerond', onPress: () => void complete(true) },
    ]);

  const parts = h?.parts ?? [];
  const current = parts[Math.min(part, Math.max(0, parts.length - 1))];
  const last = part >= parts.length - 1;

  return (
    <Screen {...refresh}>
      <BackLink label="Jureren" />
      <LargeTitleHeader title="Hoofdjury" />
      {!allowed && me.isSuccess ? (
        <EmptyState icon="uitslagen" title="Geen hoofdjury" message="Alleen de hoofdjury van het Dansgardefestival ziet hier de uitslag." />
      ) : !h ? (
        <QueryState query={head} />
      ) : !h.published ? (
        <EmptyState icon="uitslagen" title="Nog geen uitslag" message="De organisatie publiceert de uitslag zodra alle juryleden alles hebben ingediend. Trek naar beneden om te verversen." />
      ) : (
        <View style={styles.content}>
          <FilterChips
            accessibilityLabel="Deel van de prijsuitreiking"
            options={parts.map((p, i) => ({ value: String(i), label: p.name }))}
            selected={String(part)}
            onChange={(value) => setPart(Number(value))}
          />
          {current?.categories.map((c) => (
            <Card key={c.key} style={styles.card}>
              <AppText variant="cardTitle" accessibilityRole="header">
                {c.name}
              </AppText>
              <AppText variant="caption" color={colors.textSecondary}>
                Van de laatste plaats naar de winnaar
              </AppText>
              {c.rows.map((r) => (
                <View
                  key={r.entryId}
                  style={[styles.row, r.place === 1 && { backgroundColor: `${brand.yellow}40`, borderRadius: radius.sm }]}
                  accessible
                  accessibilityLabel={`${r.place}e plaats: ${r.title ? `${r.title}, ` : ''}${r.names}, ${r.club}, ${r.total} punten`}
                >
                  <AppText variant="cardTitle" style={styles.place}>
                    {r.place}e
                  </AppText>
                  <View style={styles.flex}>
                    <AppText variant="bodyStrong">{r.title ? `${r.title} · ${r.names}` : r.names}</AppText>
                    <AppText variant="caption" color={colors.textSecondary}>
                      {r.club}
                    </AppText>
                  </View>
                  <AppText variant="bodyStrong">{r.total} pnt</AppText>
                </View>
              ))}
            </Card>
          ))}
          {h.awardsCompletedAt ? (
            <View style={[styles.info, { backgroundColor: colors.tintGreen }]}>
              <AppText variant="bodyStrong">Prijsuitreiking afgerond ✓</AppText>
            </View>
          ) : last ? (
            <Button label="Prijsuitreiking afronden" disabled={busy} onPress={ask} />
          ) : (
            <Button label={`Volgende: ${parts[part + 1]?.name ?? ''}`} onPress={() => setPart(part + 1)} />
          )}
        </View>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 14, paddingBottom: 32 },
  card: { gap: 8, padding: 16, borderRadius: radius.md },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingVertical: 6, paddingHorizontal: 6 },
  place: { width: 40 },
  flex: { flex: 1 },
  info: { padding: 14, borderRadius: radius.sm },
});
