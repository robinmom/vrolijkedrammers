import { useMe, useMyNotifications, useMyRegistrations } from '../api/queries';
import { useSessionStatus } from '../auth/useSession';

/** Aantal ongelezen meldingen (rood bolletje bij het belletje en op het app-icoon); 0 voor gasten. */
export function useUnreadCount(): number {
  const status = useSessionStatus();
  const inbox = useMyNotifications();
  return status === 'signedIn' ? (inbox.data?.pages[0]?.unreadCount ?? 0) : 0;
}

/** Inschrijvingen waarvoor de optochtcommissie een aanvulling vraagt (bolletje op het Optocht-tabblad). */
export function useParadeActionCount(): number {
  const status = useSessionStatus();
  const me = useMe();
  const canRegister = (me.data?.permissions ?? []).includes('parade.register');
  const registrations = useMyRegistrations(canRegister);
  if (status !== 'signedIn' || !canRegister) {
    return 0;
  }
  return registrations.data?.filter((r) => r.status === 'AdditionalInformationRequired').length ?? 0;
}
