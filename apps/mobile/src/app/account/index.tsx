import { Redirect, router } from 'expo-router';
import * as WebBrowser from 'expo-web-browser';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { queryKeys, useMe, useMyMember } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { useSessionStatus } from '../../auth/useSession';
import { groupFunctionLabels, signOut, statusLabels } from '../../features/account';
import { formatDateOnly } from '../../lib/dates';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Badge, Card, LargeTitleHeader, QueryState, Screen, SettingsList } from '../../ui';

/**
 * Mijn gegevens (fase 9): de gegevens uit de ledenadministratie (alleen lezen), groepen en rollen, met de acties
 * rond het account. Wijzigingen lopen via het secretariaat, zodat e-Boekhouden de bron blijft (ADR-010).
 */
export default function MijnGegevensScreen() {
  const status = useSessionStatus();
  const { colors } = useTheme();
  const me = useMe();
  const member = useMyMember();
  const { refreshing, onRefresh } = useRefresh([queryKeys.me, queryKeys.myMember]);
  const [exportMessage, setExportMessage] = useState<string | null>(null);

  if (status === 'signedOut') {
    return <Redirect href="/meer/inloggen" />;
  }

  async function downloadData() {
    setExportMessage(null);
    const { data } = await api.POST('/api/v1/me/privacy/export');
    if (data?.downloadUrl) {
      await WebBrowser.openBrowserAsync(data.downloadUrl);
      setExportMessage('Je gegevens staan klaar als bestand. De link blijft 24 uur bruikbaar.');
    } else {
      setExportMessage('Het downloaden lukt nu niet. Probeer het later opnieuw.');
    }
  }

  const m = member.data;
  const rows: [string, string | null | undefined][] = m
    ? [
        ['Lidnummer', m.memberNumber],
        ['Adres', [m.addressLine, [m.postalCode, m.city].filter(Boolean).join(' ')].filter(Boolean).join(', ')],
        ['E-mailadres', m.email],
        ['Telefoon', m.phone],
        ['Geboortedatum', m.birthDate ? formatDateOnly(m.birthDate) : null],
        ['Lid sinds', m.joinYear ? String(m.joinYear) : null],
      ]
    : [];

  return (
    <Screen onRefresh={onRefresh} refreshing={refreshing}>
      <BackLink label="Meer" />
      <LargeTitleHeader title="Mijn gegevens" />
      <View style={styles.content}>
        <QueryState query={member} />
        {m ? (
          <Card style={styles.card}>
            <View style={styles.nameRow}>
              <AppText variant="cardTitle" style={styles.flex}>
                {m.fullName}
              </AppText>
              <Badge
                label={statusLabels[m.status] ?? m.status}
                variant={m.status === 'Active' ? 'youth' : 'category'}
                size="regular"
              />
            </View>
            {rows.map(([label, value]) => (
              <View key={label} style={[styles.row, { borderTopColor: colors.border }]}>
                <AppText variant="caption" color={colors.textSecondary} style={styles.label}>
                  {label}
                </AppText>
                <AppText variant="body" style={styles.flex}>
                  {value || '—'}
                </AppText>
              </View>
            ))}
            <AppText variant="caption" color={colors.textSecondary}>
              Klopt er iets niet? Geef wijzigingen door aan het secretariaat; zij passen het aan in de
              ledenadministratie.
            </AppText>
          </Card>
        ) : null}

        {m && m.groups.length > 0 ? (
          <Card style={styles.card}>
            <AppText variant="sectionHeader" accessibilityRole="header">
              Groepen
            </AppText>
            {m.groups.map((g) => (
              <AppText key={g.name} variant="body">
                {g.name} · {groupFunctionLabels[g.function] ?? g.function}
              </AppText>
            ))}
          </Card>
        ) : null}

        {me.data && me.data.roles.length > 0 ? (
          <AppText variant="caption" color={colors.textSecondary}>
            Rollen in de app: {me.data.roles.map((r) => r.name).join(', ')}
          </AppText>
        ) : null}

        <AppText variant="sectionHeader" accessibilityRole="header">
          Account
        </AppText>
        <SettingsList
          items={[
            {
              type: 'link',
              key: 'apparaten',
              label: 'Mijn apparaten',
              onPress: () => router.push('/account/apparaten'),
            },
            { type: 'link', key: 'download', label: 'Mijn gegevens downloaden', onPress: () => void downloadData() },
            {
              type: 'link',
              key: 'uitloggen',
              label: 'Uitloggen',
              onPress: () => void signOut().then(() => router.replace('/meer')),
            },
            {
              type: 'link',
              key: 'verwijderen',
              label: 'Account verwijderen',
              onPress: () => router.push('/account/verwijderen'),
            },
          ]}
        />
        {exportMessage ? (
          <AppText variant="caption" color={colors.textSecondary} accessibilityRole="alert">
            {exportMessage}
          </AppText>
        ) : null}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16 },
  card: { padding: 16, gap: 10 },
  nameRow: { flexDirection: 'row', alignItems: 'center', gap: 10 },
  row: { flexDirection: 'row', gap: 12, borderTopWidth: StyleSheet.hairlineWidth * 2, paddingTop: 10 },
  label: { width: 110 },
  flex: { flex: 1 },
});
