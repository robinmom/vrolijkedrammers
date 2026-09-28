import Constants from 'expo-constants';
import * as Device from 'expo-device';
import * as Notifications from 'expo-notifications';
import type { Href } from 'expo-router';
import { Platform } from 'react-native';
import { api } from '../api/client';
import { getInstallationId, getStatus } from '../auth/session';
import { isGuid } from '../lib/ids';

/**
 * Pushmeldingen in de app (fase 10b, ADR-009). De server kiest per categorie een Android-kanaal met dezelfde id; zo kan
 * een lid in de systeeminstellingen per soort dempen. Toestemming vragen gebeurt alleen op een logisch moment (knop in
 * Meldingen of na het inloggen), nooit bij de eerste start.
 */
export const channels = [
  {
    id: 'urgent',
    name: 'Dringend',
    description: 'Belangrijke wijzigingen, zoals een afgelaste optocht',
    importance: 'MAX',
  },
  { id: 'program', name: 'Programma', description: 'Activiteiten en wijzigingen in het programma', importance: 'HIGH' },
  { id: 'news', name: 'Nieuws', description: 'Nieuwe berichten van de vereniging', importance: 'DEFAULT' },
  { id: 'parade', name: 'Optocht', description: 'Inschrijving en informatie over de optocht', importance: 'HIGH' },
  { id: 'danceguard', name: 'Dansgarde', description: 'Trainingen en optredens van de dansgarde', importance: 'HIGH' },
  { id: 'kader', name: 'Kader', description: 'Berichten voor kaderleden', importance: 'DEFAULT' },
  { id: 'tickets', name: 'Tickets', description: 'Tickets en toegang', importance: 'DEFAULT' },
  {
    id: 'reminder',
    name: 'Herinneringen',
    description: 'Herinneringen aan activiteiten en deadlines',
    importance: 'DEFAULT',
  },
  { id: 'system', name: 'Account', description: 'Berichten over je account en apparaten', importance: 'DEFAULT' },
] as const;

export const categoryLabels: Record<string, string> = {
  Urgent: 'Dringend',
  Program: 'Programma',
  News: 'Nieuws',
  Parade: 'Optocht',
  DanceGuard: 'Dansgarde',
  Kader: 'Kader',
  Tickets: 'Tickets',
  Reminder: 'Herinneringen',
  System: 'Account',
};

/** Eénmalig bij het starten: meldingen ook tonen als de app open is, en de Android-kanalen aanmaken. */
export async function configureNotifications(): Promise<void> {
  Notifications.setNotificationHandler({
    handleNotification: async () => ({
      shouldShowBanner: true,
      shouldShowList: true,
      shouldPlaySound: true,
      shouldSetBadge: false,
    }),
  });
  if (Platform.OS === 'android') {
    for (const channel of channels) {
      await Notifications.setNotificationChannelAsync(channel.id, {
        name: channel.name,
        description: channel.description,
        importance: Notifications.AndroidImportance[channel.importance],
      });
    }
  }
}

export type PushPermission = 'granted' | 'denied' | 'undetermined' | 'unavailable';

/** `unavailable`: simulator/emulator of Android in Expo Go (daar werkt push niet sinds SDK 53). */
function pushAvailable(): boolean {
  return Device.isDevice && !(Platform.OS === 'android' && Constants.executionEnvironment === 'storeClient');
}

export async function getPushPermission(): Promise<PushPermission> {
  if (!pushAvailable()) {
    return 'unavailable';
  }
  const { status } = await Notifications.getPermissionsAsync();
  return status === 'granted' ? 'granted' : status === 'denied' ? 'denied' : 'undetermined';
}

/** Vraagt toestemming (alleen na een actie van de gebruiker) en registreert het token. */
export async function enablePush(): Promise<PushPermission> {
  if (!pushAvailable()) {
    return 'unavailable';
  }
  const { status } = await Notifications.requestPermissionsAsync();
  if (status !== 'granted') {
    return status === 'denied' ? 'denied' : 'undetermined';
  }
  await registerPushToken();
  return 'granted';
}

let lastRegistration: string | null = null;

/**
 * Meldt het Expo-token aan bij de API: ingelogd bij het huidige apparaat, anders als gast (alleen meldingen aan
 * iedereen). Opnieuw aanroepen na in- of uitloggen is veilig; de API koppelt het token dan om.
 */
export async function registerPushToken(): Promise<boolean> {
  if ((await getPushPermission()) !== 'granted') {
    return false;
  }
  const projectId = Constants.expoConfig?.extra?.eas?.projectId as string | undefined;
  const { data: token } = await Notifications.getExpoPushTokenAsync(projectId ? { projectId } : undefined);
  const signedIn = getStatus() === 'signedIn';
  const key = `${signedIn ? 'user' : 'guest'}:${token}`;
  if (key === lastRegistration) {
    return true;
  }
  if (signedIn) {
    const { data: devices } = await api.GET('/api/v1/me/devices');
    const current = devices?.find((d) => d.current);
    if (!current) {
      return false;
    }
    const { response } = await api.PUT('/api/v1/me/devices/{id}/push-token', {
      params: { path: { id: current.id } },
      body: { token },
    });
    if (!response.ok) {
      return false;
    }
  } else {
    const { response } = await api.POST('/api/v1/push-devices/anonymous', {
      body: { installId: await getInstallationId(), platform: Platform.OS === 'ios' ? 'Ios' : 'Android', token },
    });
    if (!response.ok) {
      return false;
    }
  }
  lastRegistration = key;
  return true;
}

/** Voor tests. */
export function resetPushRegistrationForTest() {
  lastRegistration = null;
}

/**
 * Link uit een melding (`drammers://…`) naar een scherm. Alleen bekende paden en GUID's; al het andere opent de inbox,
 * zodat een link nooit naar een onverwacht scherm leidt (docs/06 PLATFORM).
 */
export function routeForLink(link: string | null | undefined): Href {
  const match = /^drammers:\/\/([a-z-]+)(?:\/([^/?#]+))?\/?$/.exec(link ?? '');
  if (!match) {
    return '/meldingen';
  }
  const [, section, id] = match;
  switch (section) {
    case 'nieuws':
      return id ? (isGuid(id) ? { pathname: '/nieuws/[id]', params: { id } } : '/meldingen') : '/nieuws';
    case 'activiteit':
      return id && isGuid(id) ? { pathname: '/activiteit/[id]', params: { id } } : '/meldingen';
    case 'agenda':
    case 'programma':
      return '/programma';
    case 'optocht':
      return '/optocht';
    case 'fotos':
      return id ? (isGuid(id) ? { pathname: '/fotos/[id]', params: { id } } : '/meldingen') : '/fotos';
    default:
      return '/meldingen';
  }
}
