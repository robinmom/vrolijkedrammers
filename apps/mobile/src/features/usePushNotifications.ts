import { useQueryClient } from '@tanstack/react-query';
import * as Notifications from 'expo-notifications';
import { router } from 'expo-router';
import { useEffect, useRef } from 'react';
import { api } from '../api/client';
import { queryKeys } from '../api/queries';
import { useSessionStatus } from '../auth/useSession';
import { useUnreadCount } from './badges';
import { configureNotifications, registerPushToken, routeForLink } from './push';

/**
 * Push in de app-schil (fase 10b): kanalen en weergave instellen, het token (opnieuw) aanmelden bij elke wisseling
 * tussen gast en ingelogd, en een tik op een melding afhandelen: gelezen markeren en het juiste scherm openen, ook als
 * de app door de tik werd gestart.
 */
export function usePushNotifications() {
  const status = useSessionStatus();
  const client = useQueryClient();
  const lastResponse = Notifications.useLastNotificationResponse();
  const handled = useRef<string | null>(null);

  useEffect(() => {
    configureNotifications().catch(() => undefined);
  }, []);

  useEffect(() => {
    if (status !== 'loading') {
      registerPushToken().catch(() => undefined);
    }
  }, [status]);

  // Komt er een melding binnen terwijl de app open is: inbox (telbolletje) en inschrijvingen (status) verversen.
  useEffect(() => {
    const subscription = Notifications.addNotificationReceivedListener(() => {
      client.invalidateQueries({ queryKey: queryKeys.myNotifications }).catch(() => undefined);
      client.invalidateQueries({ queryKey: queryKeys.myRegistrations }).catch(() => undefined);
    });
    return () => subscription.remove();
  }, [client]);

  // Het rode bolletje op het app-icoon volgt het aantal ongelezen meldingen (de API zet het ook mee in elke push).
  const unread = useUnreadCount();
  useEffect(() => {
    if (status !== 'loading') {
      Notifications.setBadgeCountAsync(unread).catch(() => undefined);
    }
  }, [unread, status]);

  useEffect(() => {
    if (!lastResponse || lastResponse.actionIdentifier !== Notifications.DEFAULT_ACTION_IDENTIFIER) {
      return;
    }
    const request = lastResponse.notification.request;
    if (handled.current === request.identifier) {
      return;
    }
    handled.current = request.identifier;
    const data = request.content.data as { notificationId?: unknown; url?: unknown } | undefined;
    const id = typeof data?.notificationId === 'string' ? data.notificationId : null;
    if (id && status === 'signedIn') {
      api
        .POST('/api/v1/me/notifications/{id}/read', { params: { path: { id } } })
        .then(() => client.invalidateQueries({ queryKey: queryKeys.myNotifications }))
        .catch(() => undefined);
    }
    router.push(routeForLink(typeof data?.url === 'string' ? data.url : null));
  }, [lastResponse, status, client]);
}
