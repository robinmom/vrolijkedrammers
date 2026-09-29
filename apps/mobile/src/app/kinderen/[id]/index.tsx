import { brand } from '@drammers/design-tokens';
import { router, useLocalSearchParams } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { useChild } from '../../../api/queries';
import { isGuid } from '../../../lib/ids';
import { useTheme } from '../../../theme/ThemeProvider';
import {
  AppText,
  BackLink,
  Card,
  EmptyState,
  Icon,
  LargeTitleHeader,
  QueryState,
  Screen,
  type IconName,
} from '../../../ui';

const dateLong = new Intl.DateTimeFormat('nl-NL', {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  timeZone: 'Europe/Amsterdam',
});
const sent = new Intl.DateTimeFormat('nl-NL', {
  weekday: 'short',
  day: 'numeric',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
  timeZone: 'Europe/Amsterdam',
});

/** Kind-detail (fase 17, Figma scherm 2): QR, meldingen namens het kind, optocht en gegevens (alleen lezen). */
export default function KindScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  if (!isGuid(id)) {
    return (
      <Screen>
        <BackLink label="Mijn kinderen" />
        <EmptyState title="Kind niet gevonden" message="Dit kind is niet (meer) aan je account gekoppeld." />
      </Screen>
    );
  }
  return <Kind id={id} />;
}

function Kind({ id }: { id: string }) {
  const { colors } = useTheme();
  const detail = useChild(id);
  const d = detail.data;
  const child = d?.child;
  const first = child?.firstName ?? child?.fullName.split(' ')[0] ?? '';

  return (
    <Screen>
      <BackLink label="Mijn kinderen" />
      <LargeTitleHeader title={child?.fullName ?? 'Kind'} />
      <View style={styles.content}>
        <QueryState query={detail} notFoundTitle="Kind niet gevonden" />
        {child && d ? (
          <>
            <AppText variant="body" color={colors.textSecondary}>
              {[
                child.age !== null && child.age !== undefined ? `${child.age} jaar` : null,
                `lidnummer ${child.memberNumber}`,
              ]
                .filter(Boolean)
                .join(' · ')}
            </AppText>
            <View style={styles.tiles}>
              {child.canShowQr ? (
                <Tile
                  icon="qr"
                  tint={brand.red}
                  label={`QR van ${first}`}
                  detail="Tonen bij de deur"
                  onPress={() => router.push({ pathname: '/kinderen/[id]/qr', params: { id, naam: first } })}
                />
              ) : null}
              <Tile
                icon="meldingen"
                tint={brand.blue}
                label="Meldingen"
                detail={
                  d.notifications.length ? `${d.notifications.filter((n) => !n.readAt).length} nieuw` : 'Nog geen'
                }
                onPress={() => router.push('/meldingen')}
              />
              <Tile
                icon="optocht"
                tint={brand.green}
                label="Optocht"
                detail={
                  d.parade.length ? `${d.parade.length} inschrijving${d.parade.length === 1 ? '' : 'en'}` : 'Geen'
                }
                onPress={() => router.push('/optocht')}
              />
            </View>
            {child.ownAccount ? (
              <Card style={styles.card}>
                <AppText variant="body">
                  {first} heeft een eigen account: de QR staat op de eigen telefoon. Jij ontvangt nog meldingen namens{' '}
                  {first}
                  tot {first} 18 is.
                </AppText>
              </Card>
            ) : null}

            <Card style={styles.card}>
              <AppText variant="sectionHeader" accessibilityRole="header">
                Gegevens
              </AppText>
              <Row
                label="Geboortedatum"
                value={child.birthDate ? dateLong.format(new Date(`${child.birthDate}T12:00:00Z`)) : '–'}
              />
              {d.group ? <Row label="Groep" value={d.group} /> : null}
              {child.groups.length ? <Row label="Groepen" value={child.groups.join(', ')} /> : null}
              <Row label="Ouders/verzorgers" value={d.guardians.join(', ')} />
              <AppText variant="caption" color={colors.textSecondary}>
                Klopt er iets niet? Wijzigingen gaan via het secretariaat.
              </AppText>
            </Card>

            {d.parade.length ? (
              <Card style={styles.card}>
                <AppText variant="sectionHeader" accessibilityRole="header">
                  Optocht
                </AppText>
                {d.parade.map((p) => (
                  <View key={`${p.paradeName}-${p.groupName}`} style={styles.item}>
                    <AppText variant="bodyStrong">
                      {p.paradeName} · {p.groupName}
                    </AppText>
                    <AppText variant="caption" color={colors.textSecondary}>
                      {dateLong.format(new Date(`${p.paradeDate}T12:00:00Z`))}
                      {p.startNumber ? ` · startnummer ${p.startNumber}` : ' · startnummer volgt'}
                    </AppText>
                  </View>
                ))}
              </Card>
            ) : null}

            <Card style={styles.card}>
              <AppText variant="sectionHeader" accessibilityRole="header">
                Laatste meldingen
              </AppText>
              {d.notifications.length === 0 ? (
                <AppText variant="body" color={colors.textSecondary}>
                  Nog geen meldingen namens {first}.
                </AppText>
              ) : (
                d.notifications.map((n) => (
                  <View key={n.id} style={styles.item}>
                    <AppText variant="bodyStrong">
                      Namens {first} · {n.title}
                    </AppText>
                    <AppText variant="caption" color={colors.textSecondary}>
                      {sent.format(new Date(n.sentAt))}
                    </AppText>
                  </View>
                ))
              )}
            </Card>
          </>
        ) : null}
      </View>
    </Screen>
  );
}

function Tile({
  icon,
  tint,
  label,
  detail,
  onPress,
}: {
  icon: IconName;
  tint: string;
  label: string;
  detail: string;
  onPress: () => void;
}) {
  const { colors } = useTheme();
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={`${label}. ${detail}`}
      style={styles.tileWrap}
    >
      <Card style={styles.tile}>
        <View style={[styles.tileIcon, { backgroundColor: tint }]}>
          <Icon name={icon} size={20} color="#FFFFFF" />
        </View>
        <AppText variant="bodyStrong" numberOfLines={2}>
          {label}
        </AppText>
        <AppText variant="caption" color={colors.textSecondary}>
          {detail}
        </AppText>
      </Card>
    </Pressable>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.row}>
      <AppText variant="body" color={colors.textSecondary} style={styles.flex}>
        {label}
      </AppText>
      <AppText variant="bodyStrong" style={styles.value}>
        {value}
      </AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16, paddingBottom: 24 },
  card: { padding: 16, gap: 10 },
  tiles: { flexDirection: 'row', gap: 10 },
  tileWrap: { flex: 1 },
  tile: { padding: 12, gap: 4, minHeight: 112 },
  tileIcon: { width: 36, height: 36, borderRadius: 10, alignItems: 'center', justifyContent: 'center' },
  row: { flexDirection: 'row', gap: 8 },
  flex: { flex: 1 },
  value: { flexShrink: 1, textAlign: 'right' },
  item: { gap: 2 },
});
