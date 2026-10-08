import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { Linking, Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { queryKeys, useMyAdvertisers } from '../../api/queries';
import { brand } from '@drammers/design-tokens';
import { useTheme } from '../../theme/ThemeProvider';
import {
  AppText,
  BackLink,
  Button,
  Card,
  CheckboxRow,
  FilterChips,
  LargeTitleHeader,
  QueryState,
  Screen,
  SearchField,
  TextField,
} from '../../ui';

type Advertiser = NonNullable<ReturnType<typeof useMyAdvertisers>['data']>['items'][number];
type Filter = 'Open' | 'Collected' | 'Stopped' | 'All';

const euro = new Intl.NumberFormat('nl-NL', { style: 'currency', currency: 'EUR' });
const money = (value: number | null | undefined) => (value === null || value === undefined ? null : euro.format(value));

/** Jaar Y is het carnavalsjaar (Y-1)/Y (zoals BIJDRAGE Y in het Excel-overzicht). */
export const season = (year: number) => `${year - 1}/${year}`;

const PAYMENT: Record<Advertiser['payment'], string> = {
  Mandate: 'Betaalt via machtiging (incasso).',
  Cash: 'Betaalt contant.',
  Invoice: 'Betaalt op rekening (factuur).',
};

const longDate = new Intl.DateTimeFormat('nl-NL', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' });
const dayMonth = new Intl.DateTimeFormat('nl-NL', { day: 'numeric', month: 'long', timeZone: 'UTC' });

/** De collectieperiode (fase 27i), bijv. "Ophalen van 12 oktober t/m 1 december 2026". */
export function period(start?: string | null, end?: string | null) {
  const date = (d: string) => new Date(`${d}T00:00:00Z`);
  if (start && end) return `Ophalen van ${dayMonth.format(date(start))} t/m ${longDate.format(date(end))}`;
  if (end) return `Ophalen tot en met ${longDate.format(date(end))}`;
  if (start) return `Ophalen vanaf ${longDate.format(date(start))}`;
  return null;
}

const STATUS: Record<Advertiser['status'], string> = { Open: 'Nog langs', Collected: 'Opgehaald', Stopped: 'Stopt' };

/**
 * Adverteerders ophalen (fase 27b-2): de collectant ziet zijn adverteerders van de lopende campagne en vinkt af wie
 * weer meedoet (opgehaald, eventueel met een ander bedrag) en wie stopt. Een nieuwe adverteerder meldt hij hier aan.
 */
export default function AdverteerdersScreen() {
  const { colors } = useTheme();
  const mine = useMyAdvertisers();
  const [filter, setFilter] = useState<Filter>('Open');
  const [search, setSearch] = useState('');
  const [showInfo, setShowInfo] = useState(false);

  const items = mine.data?.items ?? [];
  const done = items.filter((a) => a.status !== 'Open').length;
  const shown = items
    .filter((a) => filter === 'All' || a.status === filter)
    .filter((a) => !search || `${a.companyName} ${a.city ?? ''} ${a.contactName ?? ''}`.toLowerCase().includes(search.toLowerCase()));

  return (
    <Screen>
      <BackLink label="Meer" />
      <LargeTitleHeader title={`Adverteerders ${mine.data ? season(mine.data.year) : ''}`} />
      <View style={styles.content}>
        {!mine.data ? (
          <QueryState query={mine} />
        ) : !mine.data.isCollector ? (
          <AppText variant="body" color={colors.textSecondary}>
            Adverteerders ophalen kan alleen als je in het kader zit of collectant bent.
          </AppText>
        ) : (
          <>
            <Card style={styles.card}>
              <AppText variant="bodyStrong">
                {done} van {items.length} afgehandeld
              </AppText>
              {period(mine.data.startDate, mine.data.endDate) ? (
                <AppText variant="caption" color={colors.textSecondary}>
                  {period(mine.data.startDate, mine.data.endDate)}
                </AppText>
              ) : null}
              <View
                style={[styles.track, { backgroundColor: colors.surfaceMuted }]}
                accessibilityRole="progressbar"
                accessibilityValue={{ min: 0, max: items.length || 1, now: done }}
              >
                <View style={[styles.bar, { width: `${items.length ? (100 * done) / items.length : 0}%` }]} />
              </View>
            </Card>
            <Button
              label={showInfo ? 'Info verbergen' : 'Info'}
              variant="secondary"
              onPress={() => setShowInfo(!showInfo)}
            />
            {showInfo ? (
              <Card style={styles.card}>
                <AppText variant="bodyStrong">Informatie {season(mine.data.year)}</AppText>
                <AppText variant="body" color={mine.data.info ? colors.textPrimary : colors.textSecondary}>
                  {mine.data.info ?? 'Er is nog geen informatie voor deze campagne.'}
                </AppText>
              </Card>
            ) : null}
            <Button label="Nieuwe adverteerder" icon="plus" variant="secondary" onPress={() => router.push('/adverteerders/nieuw')} />
            <FilterChips<Filter>
              accessibilityLabel="Toon"
              options={[
                { value: 'Open', label: `Nog langs (${items.length - done})` },
                { value: 'Collected', label: 'Opgehaald' },
                { value: 'Stopped', label: 'Stopt' },
                { value: 'All', label: 'Alle' },
              ]}
              selected={filter}
              onChange={setFilter}
            />
            <SearchField value={search} onChangeText={setSearch} placeholder="Zoek op naam of plaats" autoFocus={false} />
            {shown.map((a) => (
              <AdvertiserCard key={a.id} advertiser={a} />
            ))}
            {shown.length === 0 ? (
              <AppText variant="body" color={colors.textSecondary}>
                {filter === 'Open' && items.length > 0 ? 'Je bent overal langs geweest. Alaaf!' : 'Geen adverteerders.'}
              </AppText>
            ) : null}
          </>
        )}
      </View>
    </Screen>
  );
}

function AdvertiserCard({ advertiser: a }: { advertiser: Advertiser }) {
  const { colors } = useTheme();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [amount, setAmount] = useState(a.amount !== null ? String(a.amount) : a.previousAmount !== null ? String(a.previousAmount) : '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const cash = a.payment === 'Cash';
  const [received, setReceived] = useState(a.cashReceived);
  const [note, setNote] = useState(a.note ?? '');
  const [saved, setSaved] = useState(false);

  async function save(status: Advertiser['status']) {
    const value = amount.trim() ? Number(amount.replace(',', '.')) : null;
    if (status === 'Collected' && value !== null && (Number.isNaN(value) || value < 0)) {
      setError('Vul een geldig bedrag in.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const { response, error: problem } = await api.PUT('/api/v1/me/advertisers/{id}/status', {
        params: { path: { id: a.id } },
        body: { status, amount: status === 'Collected' ? value : null, note: note.trim() || null, cashReceived: cash ? received : null },
      });
      if (response.ok) {
        setOpen(false);
        await queryClient.invalidateQueries({ queryKey: queryKeys.myAdvertisers });
      } else {
        setError((problem as { detail?: string } | undefined)?.detail ?? 'Opslaan lukt nu niet.');
      }
    } catch {
      setError('Geen verbinding. Probeer het opnieuw.');
    } finally {
      setBusy(false);
    }
  }

  /** Alleen de opmerking opslaan (fase 27g); de stand blijft gelijk. */
  async function saveNote() {
    setBusy(true);
    setError(null);
    setSaved(false);
    try {
      const { response, error: problem } = await api.PUT('/api/v1/me/advertisers/{id}/note', {
        params: { path: { id: a.id } },
        body: { note: note.trim() || null },
      });
      if (response.ok) {
        setSaved(true);
        await queryClient.invalidateQueries({ queryKey: queryKeys.myAdvertisers });
      } else {
        setError((problem as { detail?: string } | undefined)?.detail ?? 'Opslaan lukt nu niet.');
      }
    } catch {
      setError('Geen verbinding. Probeer het opnieuw.');
    } finally {
      setBusy(false);
    }
  }

  const statusColor = a.status === 'Collected' ? colors.successText : a.status === 'Stopped' ? colors.textSecondary : colors.accentText;
  return (
    <Card style={styles.card}>
      <Pressable
        onPress={() => setOpen(!open)}
        accessibilityRole="button"
        accessibilityState={{ expanded: open }}
        accessibilityLabel={`${a.companyName}, ${STATUS[a.status]}`}
        style={styles.header}
      >
        <View style={styles.grow}>
          <AppText variant="listTitle">{a.companyName}</AppText>
          <AppText variant="caption" color={colors.textSecondary}>
            {[a.page ? `Pagina ${a.page}` : null, a.contactName, a.city].filter(Boolean).join(' · ') || ' '}
          </AppText>
        </View>
        <View style={styles.right}>
          <AppText variant="label" color={statusColor}>
            {STATUS[a.status]}
          </AppText>
          <AppText variant="caption" color={colors.textSecondary}>
            {money(a.status === 'Collected' ? a.amount : a.previousAmount) ?? ''}
            {cash && a.status === 'Collected' ? (a.cashReceived ? ' · ontvangen' : ' · nog te ontvangen') : ''}
          </AppText>
        </View>
      </Pressable>
      <Contact advertiser={a} />
      {open ? (
        <View style={styles.details}>
          {a.addressLine ? (
            <AppText variant="body">
              {a.addressLine}
              {a.postalCode || a.city ? `, ${[a.postalCode, a.city].filter(Boolean).join(' ')}` : ''}
            </AppText>
          ) : null}
          <AppText variant="caption" color={colors.textSecondary}>
            {PAYMENT[a.payment]}
          </AppText>
          {a.history && a.history.length > 0 ? (
            <View style={styles.history} accessibilityLabel="Eerdere jaren">
              {a.history.map((h) => (
                <View key={h.year} style={styles.historyRow}>
                  <AppText variant="caption" color={colors.textSecondary}>
                    {season(h.year)}
                  </AppText>
                  <AppText variant="caption">
                    {h.isFree ? 'gratis' : h.status === 'Stopped' ? 'stopte' : h.status === 'Open' ? '—' : (money(h.amount) ?? '—')}
                  </AppText>
                </View>
              ))}
            </View>
          ) : (
            <AppText variant="caption" color={colors.textSecondary}>
              Geen eerdere bijdragen bekend.
            </AppText>
          )}
          <TextField label="Bedrag dit jaar (€)" value={amount} onChangeText={setAmount} keyboardType="decimal-pad" maxLength={10} />
          {cash ? <CheckboxRow label="Geld contant ontvangen" checked={received} onChange={setReceived} /> : null}
          <TextField
            label="Opmerking"
            value={note}
            onChangeText={(v) => {
              setNote(v);
              setSaved(false);
            }}
            multiline
            maxLength={500}
          />
          <Button label={saved ? 'Opmerking opgeslagen' : 'Opmerking opslaan'} variant="secondary" onPress={() => void saveNote()} disabled={busy} />
          {error ? (
            <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
              {error}
            </AppText>
          ) : null}
          <Button label="Afvinken als opgehaald" onPress={() => void save('Collected')} disabled={busy} />
          <Button label="Stopt dit jaar" variant="secondary" onPress={() => void save('Stopped')} disabled={busy} />
          {a.status !== 'Open' ? <Button label="Toch nog langs" variant="secondary" onPress={() => void save('Open')} disabled={busy} /> : null}
        </View>
      ) : null}
    </Card>
  );
}

/** Contactgegevens in het overzicht: telefoon en e-mail zijn aan te tikken. */
function Contact({ advertiser: a }: { advertiser: Advertiser }) {
  const { colors } = useTheme();
  const phones = [...new Set([a.mobile, a.phone].filter((p): p is string => !!p))];
  if (phones.length === 0 && !a.email) return null;
  return (
    <View style={styles.contact}>
      {phones.map((p) => (
        <Pressable key={p} onPress={() => void Linking.openURL(`tel:${p.replace(/\s/g, '')}`)} accessibilityRole="link" accessibilityLabel={`Bel ${p}`}>
          <AppText variant="caption" color={colors.linkText}>
            {p}
          </AppText>
        </Pressable>
      ))}
      {a.email ? (
        <Pressable onPress={() => void Linking.openURL(`mailto:${a.email}`)} accessibilityRole="link" accessibilityLabel={`Mail ${a.email}`}>
          <AppText variant="caption" color={colors.linkText}>
            {a.email}
          </AppText>
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  contact: { flexDirection: 'row', flexWrap: 'wrap', columnGap: 16, rowGap: 4 },
  content: { paddingHorizontal: 20, gap: 12, paddingBottom: 24 },
  card: { padding: 16, gap: 10 },
  track: { height: 8, borderRadius: 6, overflow: 'hidden' },
  bar: { height: 8, borderRadius: 6, backgroundColor: brand.green },
  header: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  grow: { flex: 1, gap: 2 },
  right: { alignItems: 'flex-end', gap: 2 },
  details: { gap: 10 },
  history: { gap: 2 },
  historyRow: { flexDirection: 'row', justifyContent: 'space-between' },
});
