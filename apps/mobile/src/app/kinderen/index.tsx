import { brand } from '@drammers/design-tokens';
import { router } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { useMyChildren, useMyGuardianRequests } from '../../api/queries';
import { useSessionStatus } from '../../auth/useSession';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, EmptyState, Icon, LargeTitleHeader, QueryState, Screen } from '../../ui';

type Child = NonNullable<ReturnType<typeof useMyChildren>['data']>[number];

const date = new Intl.DateTimeFormat('nl-NL', { day: 'numeric', month: 'short', timeZone: 'Europe/Amsterdam' });

/**
 * Meer → Mijn kinderen (fase 17, Figma 👨‍👧 Ouders & kinderen scherm 1): leden die bij deze ouder horen, met de QR,
 * meldingen namens het kind en inschrijvingen. Plus openstaande koppelverzoeken en "Kind koppelen aanvragen".
 */
export default function MijnKinderenScreen() {
  const { colors } = useTheme();
  const status = useSessionStatus();
  const children = useMyChildren();
  const requests = useMyGuardianRequests();
  const pending = (requests.data ?? []).filter((r) => r.status === 'Pending');

  if (status !== 'signedIn') {
    return (
      <Screen>
        <BackLink label="Meer" />
        <LargeTitleHeader title="Mijn kinderen" />
        <EmptyState
          title="Log eerst in"
          message="Log in met je account; daarna zie je hier de kinderen die bij jou horen."
          action={{ label: 'Inloggen', onPress: () => router.push('/meer/inloggen') }}
        />
      </Screen>
    );
  }

  return (
    <Screen>
      <BackLink label="Meer" />
      <LargeTitleHeader title="Mijn kinderen" />
      <View style={styles.content}>
        <AppText variant="body" color={colors.textSecondary}>
          Leden die bij jou horen. Je ziet hun gegevens, meldingen en inschrijvingen, en je kunt hun QR tonen bij de
          ingang.
        </AppText>
        <QueryState query={children} />
        {children.data?.length === 0 && pending.length === 0 ? (
          <Card style={styles.card}>
            <AppText variant="body">Er zijn nog geen kinderen aan je account gekoppeld.</AppText>
          </Card>
        ) : null}
        {(children.data ?? []).map((child) => (
          <ChildRow key={child.memberId} child={child} />
        ))}
        {pending.map((r) => (
          <Card key={r.id} style={[styles.card, { backgroundColor: colors.tintYellow }]}>
            <View style={[styles.pill, { backgroundColor: brand.yellow }]}>
              <AppText variant="label" color={brand.navy}>
                Wacht op bestuur
              </AppText>
            </View>
            <AppText variant="body">
              Koppelverzoek voor &quot;{r.childFirstName} {r.childLastName}&quot;, verstuurd op{' '}
              {date.format(new Date(r.createdAt))}. Je krijgt een melding zodra het bestuur het heeft bekeken.
            </AppText>
          </Card>
        ))}
        <Button label="Kind koppelen aanvragen" variant="secondary" onPress={() => router.push('/kinderen/koppelen')} />
        <AppText variant="caption" color={colors.textSecondary}>
          Maximaal 2 ouders of verzorgers per kind. Vanaf 15 jaar kan het bestuur je kind een eigen account geven.
        </AppText>
      </View>
    </Screen>
  );
}

function ChildRow({ child }: { child: Child }) {
  const { colors } = useTheme();
  const initials = child.fullName
    .split(' ')
    .filter(Boolean)
    .map((p) => p[0])
    .slice(0, 2)
    .join('')
    .toUpperCase();
  const detail = [
    child.age !== null && child.age !== undefined ? `${child.age} jaar` : null,
    child.groups.join(', ') || null,
  ]
    .filter(Boolean)
    .join(' · ');
  return (
    <Pressable
      onPress={() => router.push({ pathname: '/kinderen/[id]', params: { id: child.memberId } })}
      accessibilityRole="button"
      accessibilityLabel={`${child.fullName}. ${detail}${child.ownAccount ? '. Heeft een eigen account' : ''}`}
    >
      <Card style={[styles.card, styles.row]}>
        <View style={[styles.avatar, { backgroundColor: child.ownAccount ? brand.blue : brand.red }]}>
          <AppText variant="bodyStrong" color="#FFFFFF">
            {initials}
          </AppText>
        </View>
        <View style={styles.flex}>
          <AppText variant="cardTitle">{child.fullName}</AppText>
          {detail ? (
            <AppText variant="caption" color={colors.textSecondary}>
              {detail}
            </AppText>
          ) : null}
          <View style={styles.pills}>
            {child.ownAccount ? (
              <View style={[styles.pill, { backgroundColor: colors.tintBlue }]}>
                <AppText variant="label" color={colors.linkText}>
                  Eigen account
                </AppText>
              </View>
            ) : child.canShowQr ? (
              <View style={[styles.pill, { backgroundColor: colors.tintGreen }]}>
                <AppText variant="label" color={colors.successText}>
                  QR beschikbaar
                </AppText>
              </View>
            ) : null}
          </View>
        </View>
        <Icon name="chevron" size={20} />
      </Card>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16, paddingBottom: 24 },
  card: { padding: 16, gap: 10 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  flex: { flex: 1, gap: 4 },
  avatar: { width: 48, height: 48, borderRadius: 24, alignItems: 'center', justifyContent: 'center' },
  pills: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  pill: { alignSelf: 'flex-start', borderRadius: 999, paddingHorizontal: 10, paddingVertical: 3 },
});
