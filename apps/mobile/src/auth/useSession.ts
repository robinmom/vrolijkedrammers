import { useSyncExternalStore } from 'react';
import { getStatus, subscribe, type SessionStatus } from './session';

/** Aanmeldstatus voor schermen; `loading` tot de Keychain/Keystore is gelezen. */
export function useSessionStatus(): SessionStatus {
  return useSyncExternalStore(subscribe, getStatus, getStatus);
}
