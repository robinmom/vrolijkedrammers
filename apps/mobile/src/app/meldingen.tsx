import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { AppState, Linking, Pressable, StyleSheet, View } from 'react-native';
import { api, type NotificationCategory } from '../api/client';
import { queryKeys, useMyNotificationPreferences, useMyNotifications } from '../api/queries';
import { useRefresh } from '../api/useRefresh';
import { useSessionStatus } from '../auth/useSession';
import { categoryLabels, enablePush, getPushPermission, routeForLink, type PushPermission } from '../features/push';
import { notificationMoment } from '../lib/dates';
import { useTheme } from '../theme/ThemeProvider';
import {
  AppText,
  BackLink,
  Button,
  Card,
  EmptyState,
  LargeTitleHeader,
  Screen,
  SectionHeader,
  SettingsList,
} from '../ui';

/** Toestemming voor push: aanzetten (vraagt het OS), of naar de systeeminstellingen als het eerder is geweigerd. */
function PushCard() {
  const { colors } = useTheme();
  const [permission, setPermission] = useState<PushPermission | null>(null);
  const refresh = useCallback(() => {
    getPushPermission().then(setPermission, () => setPermission('unavailable'));
  }, []);
  useEffect(() => {
    refresh();
    // Terug uit de systeeminstellingen: opnieuw kijken.
    const subscription = AppState.addEventListener('change', (state) => state === 'active' && refresh());
    return () => subscription.remove();
  }, [refresh]);

  if (permission === null || permission === 'granted') {
    return null;
  }
  return (
    <Card style={styles.card}>
      <AppText variant="bodyStrong">Pushmeldingen</AppText>
      <AppText variant="body" color={colors.textSecondary}>
        {permission === 'unavailable'
          ? 'Op dit toestel kan de app geen pushmeldingen ontvangen. Je berichten staan wel hieronder.'
          : permission === 'denied'
            ? 'Pushmeldingen staan uit voor deze app. Zet ze aan in de instellingen van je telefoon.'
            : 'Ontvang direct een melding bij wijzigingen in het programma, nieuws en dringende berichten.'}
      </AppText>
      {permission === 'undetermined' ? (
        <Button label="Pushmeldingen aanzetten" onPress={() => enablePush().then(setPermission, refresh)} />
      ) : permission === 'denied' ? (
        <Button label="Instellingen openen" variant="secondary" onPress={() => void Linking.openSettings()} />
      ) : null}
    </Card>
  );
}

/** Per categorie aan of uit; Dringend en Account staan altijd aan. */
function Preferences() {
  const preferences = useMyNotificationPreferences();
  const client = useQueryClient();
  if (!preferences.data) {
    return null;
  }
  async function change(category: NotificationCategory, enabled: boolean) {
    const { data } = await api.PUT('/api/v1/me/notification-preferences', {
      body: { preferences: [{ category, enabled }] },
    });
    if (data) {
      client.setQueryData(queryKeys.myNotificationPreferences, data);
    }
  }
  return (
    <>
      <SectionHeader title="Welke meldingen wil je?" />
      <SettingsList
        items={preferences.data
          .filter((p) => p.canDisable)
          .map((p) => ({
            type: 'toggle' as const,
            key: p.category,
            label: categoryLabels[p.category] ?? p.category,
            value: p.enabled,
            onValueChange: (value: boolean) => void change(p.category, value),
          }))}
      />
    </>
  );
}

/**
 * Meldingen (fase 10): de inbox met gelezen/ongelezen, toestemming voor push en voorkeuren per categorie. Een melding
 * met een link opent het bijbehorende scherm. Gasten ontvangen alleen algemene meldingen en hebben geen inbox.
 */
export default function MeldingenScreen() {
  const { colors } = useTheme();
  const status = useSessionStatus();
  const inbox = useMyNotifications();
  const client = useQueryClient();
  const { refreshing, onRefresh } = useRefresh([queryKeys.myNotifications, queryKeys.myNotificationPreferences]);
  const items = inbox.data?.pages.flatMap((p) => p.items) ?? [];
  const unread = inbox.data?.pages[0]?.unreadCount ?? 0;
  const now = new Date();

  async function open(id: string, link: string | null | undefined, read: boolean) {
    if (!read) {
      await api.POST('/api/v1/me/notifications/{id}/read', { params: { path: { id } } }).catch(() => undefined);
      await client.invalidateQueries({ queryKey: queryKeys.myNotifications });
    }
    if (link) {
      router.push(routeForLink(link));
    }
  }

  async function markAllRead() {
    await api.POST('/api/v1/me/notifications/read-all').catch(() => undefined);
    await client.invalidateQueries({ queryKey: queryKeys.myNotifications });
  }

  return (
    <Screen onRefresh={status === 'signedIn' ? onRefresh : undefined} refreshing={refreshing}>
      <BackLink label="Terug" />
      <LargeTitleHeader title="Meldingen" />
      <View style={styles.content}>
        <PushCard />
        {status !== 'signedIn' ? (
          <Card style={styles.card}>
            <AppText variant="body" color={colors.textSecondary}>
              Als gast ontvang je algemene berichten van de vereniging. Log in om je persoonlijke meldingen hier terug
              te lezen en te kiezen welke je wilt ontvangen.
            </AppText>
            <Button label="Inloggen" variant="secondary" onPress={() => router.push('/meer/inloggen')} />
          </Card>
        ) : (
          <>
            {unread > 0 ? (
              <SectionHeader
                title={`${unread} ongelezen`}
                linkLabel="Alles gelezen"
                onLinkPress={() => void markAllRead()}
              />
            ) : null}
            {inbox.isSuccess && items.length === 0 ? (
              <EmptyState
                icon="meldingen"
                title="Nog geen meldingen"
                message="Berichten van de vereniging verschijnen hier."
              />
            ) : null}
            {items.map((n) => (
              <Pressable
                key={n.id}
                onPress={() => void open(n.id, n.deepLink, n.readAt != null)}
                accessibilityRole="button"
                accessibilityLabel={`${n.readAt ? '' : 'Ongelezen: '}${n.title}. ${n.body}`}
              >
                <Card style={[styles.card, !n.readAt && { borderLeftWidth: 4, borderLeftColor: colors.accentText }]}>
                  <View style={styles.meta}>
                    <AppText variant="caption" color={colors.textSecondary}>
                      {categoryLabels[n.category] ?? n.category} · {notificationMoment(n.sentAt, now)}
                    </AppText>
                  </View>
                  <AppText variant="bodyStrong">{n.title}</AppText>
                  <AppText variant="body" color={colors.textSecondary}>
                    {n.body}
                  </AppText>
                </Card>
              </Pressable>
            ))}
            {inbox.hasNextPage ? (
              <Button
                label="Oudere meldingen"
                variant="secondary"
                onPress={() => void inbox.fetchNextPage()}
                disabled={inbox.isFetchingNextPage}
              />
            ) : null}
            <Preferences />
          </>
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 12, paddingBottom: 24 },
  card: { padding: 16, gap: 6 },
  meta: { flexDirection: 'row', justifyContent: 'space-between' },
});
