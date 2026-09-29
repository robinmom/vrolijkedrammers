import { brand } from '@drammers/design-tokens';
import { useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { SaleStepperRow } from '../../features/SaleParts';
import { formatDay, problemMessage, salesKeys, useMyOrders, useShareCandidates } from '../../features/sales';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, EmptyState, LargeTitleHeader, QueryState, Screen } from '../../ui';

/**
 * Kaart delen (fase 19b, Figma "iOS / 7"): kaarten uit de groeps-QR naar een lid van dezelfde groep (vrij veld 3). Die
 * krijgt een eigen QR in de app; bij de besteller verdwijnen die kaarten uit de QR. Minstens één kaart blijft over.
 */
export default function DelenScreen() {
  const { colors } = useTheme();
  const params = useLocalSearchParams<{ ticket: string; order: string }>();
  const orders = useMyOrders();
  const candidates = useShareCandidates(params.ticket ?? null);
  const queryClient = useQueryClient();
  const order = orders.data?.find((o) => o.id === params.order);
  const ticket = order?.tickets.find((t) => t.id === params.ticket);
  const [memberId, setMemberId] = useState<string | null>(null);
  const [quantity, setQuantity] = useState(1);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const chosen = candidates.data?.find((c) => c.memberId === memberId);
  const firstName = chosen?.name.split(' ')[0] ?? '';

  async function share() {
    if (!ticket || !memberId) return;
    setBusy(true);
    setError(null);
    try {
      const { error: problem, response } = await api.POST('/api/v1/me/orders/tickets/{ticketId}/share', {
        params: { path: { ticketId: ticket.id } },
        body: { memberId, quantity },
      });
      if (!response.ok) {
        setError(problemMessage(problem, response.status));
        return;
      }
      await queryClient.invalidateQueries({ queryKey: salesKeys.myOrders });
      router.back();
    } catch {
      setError('Geen verbinding. Probeer het opnieuw.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen>
      <BackLink label="Mijn kaarten" />
      <LargeTitleHeader title="Kaart delen" />
      <View style={styles.content}>
        {!ticket || !candidates.data ? (
          <QueryState query={candidates.data ? orders : candidates} />
        ) : (
          <>
            <AppText variant="body" color={colors.textSecondary}>
              Kies een lid van {order?.groupName ? `de ${order.groupName}` : 'je groep'}. Delen kan alleen binnen je eigen groep.
            </AppText>
            {candidates.data.length === 0 ? (
              <EmptyState title="Geen groepsleden" message="Er zijn geen andere actieve leden in je groep." />
            ) : (
              <View style={styles.list} accessibilityRole="radiogroup">
                {candidates.data.map((c) => {
                  const active = c.memberId === memberId;
                  return (
                    <Pressable
                      key={c.memberId}
                      onPress={() => setMemberId(c.memberId)}
                      disabled={!c.hasAccount}
                      accessibilityRole="radio"
                      accessibilityState={{ checked: active, disabled: !c.hasAccount }}
                      accessibilityLabel={`${c.name}${c.hasTicket ? ', heeft al een kaart' : ''}${c.hasAccount ? '' : ', heeft de app niet'}`}
                    >
                      <Card style={[styles.row, active && { borderColor: brand.red, borderWidth: 2 }, !c.hasAccount && styles.disabled]}>
                        <View style={[styles.radio, { borderColor: active ? brand.red : colors.border }]}>
                          {active ? <View style={styles.radioDot} /> : null}
                        </View>
                        <AppText variant="cardTitle" style={styles.full}>
                          {c.name}
                        </AppText>
                        <AppText variant="label" color={c.hasAccount ? colors.textSecondary : colors.accentText}>
                          {!c.hasAccount ? 'Geen app' : c.hasTicket ? 'Heeft al een kaart' : ''}
                        </AppText>
                      </Card>
                    </Pressable>
                  );
                })}
              </View>
            )}
            <Card style={styles.card}>
              <AppText variant="cardTitle">Hoeveel kaarten?</AppText>
              <AppText variant="caption" color={colors.textSecondary}>
                {order?.productName} {formatDay(order?.date)}, {ticket.quantity} kaarten.
                {firstName ? ` ${firstName} krijgt een eigen QR in de app; die kaart verdwijnt bij jou.` : ''}
              </AppText>
              <SaleStepperRow
                label={firstName ? `Kaarten voor ${firstName}` : 'Kaarten'}
                hint={`max ${ticket.quantity - 1}`}
                value={quantity}
                min={1}
                max={ticket.quantity - 1}
                onChange={setQuantity}
              />
            </Card>
            <View style={styles.total}>
              <AppText variant="bodyStrong">Jij houdt {ticket.quantity - quantity} kaarten over</AppText>
              {firstName ? <AppText variant="bodyStrong">{quantity} naar {firstName}</AppText> : null}
            </View>
            {error ? (
              <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
                {error}
              </AppText>
            ) : null}
            <Button
              label={busy ? 'Even geduld…' : firstName ? `Delen met ${firstName}` : 'Kies een groepslid'}
              onPress={() => void share()}
              disabled={!memberId || busy}
            />
          </>
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 14, paddingBottom: 24 },
  list: { gap: 10 },
  row: { padding: 14, flexDirection: 'row', alignItems: 'center', gap: 12, borderWidth: 1, borderColor: 'transparent' },
  disabled: { opacity: 0.55 },
  radio: { width: 22, height: 22, borderRadius: 11, borderWidth: 2, alignItems: 'center', justifyContent: 'center' },
  radioDot: { width: 10, height: 10, borderRadius: 5, backgroundColor: brand.red },
  full: { flex: 1 },
  card: { padding: 16, gap: 10 },
  total: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
});
