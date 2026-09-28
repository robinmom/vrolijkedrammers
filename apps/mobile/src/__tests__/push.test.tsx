import { fireEvent, screen } from '@testing-library/react-native';
import { Text } from 'react-native';
import * as Notifications from 'expo-notifications';
import MeldingenScreen from '../app/meldingen';
import { setSessionForTest } from '../auth/session';
import { enablePush, registerPushToken, resetPushRegistrationForTest, routeForLink } from '../features/push';
import { notificationMoment } from '../lib/dates';
import { mockApi, renderApp } from '../test/render';

let mockIsDevice = true;
jest.mock('expo-device', () => ({
  get isDevice() {
    return mockIsDevice;
  },
  modelName: 'Pixel',
}));

const newsId = '11111111-2222-3333-4444-555555555555';
/**
 * Zelfde routes in elke test. De router van expo-router/testing-library onthoudt de navigatie van een eerdere test; daarom
 * staat de test die naar een ander scherm navigeert (inbox) na de andere schermtests.
 */
const routes = { meldingen: MeldingenScreen, 'nieuws/[id]': () => <Text>Nieuwsdetail</Text> };

const inbox = {
  items: [
    {
      id: 'aaaaaaaa-0000-0000-0000-000000000001',
      title: 'Optocht gaat door',
      body: 'Het weer is goed.',
      category: 'Urgent',
      deepLink: null,
      sentAt: '2026-09-28T10:00:00Z',
      readAt: null,
    },
    {
      id: 'aaaaaaaa-0000-0000-0000-000000000002',
      title: 'Nieuwe prins',
      body: 'Lees het nieuws.',
      category: 'News',
      deepLink: `drammers://nieuws/${newsId}`,
      sentAt: '2026-09-20T10:00:00Z',
      readAt: '2026-09-20T11:00:00Z',
    },
  ],
  unreadCount: 1,
  hasMore: false,
};
const preferences = [
  { category: 'Urgent', enabled: true, canDisable: false },
  { category: 'News', enabled: true, canDisable: true },
  { category: 'Program', enabled: false, canDisable: true },
];

describe('push en meldingen (fase 10b)', () => {
  beforeEach(() => {
    resetPushRegistrationForTest();
    mockIsDevice = true;
    (Notifications.getPermissionsAsync as jest.Mock).mockResolvedValue({ status: 'undetermined' });
  });

  it('links uit een melding openen alleen bekende schermen', () => {
    expect(routeForLink(`drammers://nieuws/${newsId}`)).toEqual({ pathname: '/nieuws/[id]', params: { id: newsId } });
    expect(routeForLink('drammers://agenda')).toBe('/programma');
    expect(routeForLink('drammers://nieuws/../../geheim')).toBe('/meldingen');
    expect(routeForLink('https://evil.example')).toBe('/meldingen');
    expect(routeForLink(null)).toBe('/meldingen');
  });

  it('moment van een melding: vandaag, gisteren of datum', () => {
    const now = new Date('2026-09-28T12:00:00Z');
    expect(notificationMoment('2026-09-28T08:05:00Z', now)).toBe('vandaag 10:05');
    expect(notificationMoment('2026-09-27T07:10:00Z', now)).toBe('gisteren 09:10');
    expect(notificationMoment('2026-09-20T10:00:00Z', now)).toBe('20 sep 2026');
  });

  it('als gast: toestemming vragen en het token anoniem aanmelden', async () => {
    setSessionForTest('signedOut', null);
    const calls = mockApi({ '/api/v1/push-devices/anonymous': { status: 204 } });
    (Notifications.getPermissionsAsync as jest.Mock).mockResolvedValue({ status: 'granted' });

    expect(await enablePush()).toBe('granted');
    expect(calls).toContain('/api/v1/push-devices/anonymous');
  });

  it('ingelogd: het token hoort bij het huidige apparaat, één keer per sessie', async () => {
    setSessionForTest('signedIn');
    (Notifications.getPermissionsAsync as jest.Mock).mockResolvedValue({ status: 'granted' });
    const calls = mockApi({
      '/api/v1/me/devices': [
        { id: 'dev-1', current: true, name: 'Pixel', platform: 'Android', lastSeenAt: '2026-09-28T10:00:00Z' },
      ],
      '/api/v1/me/devices/dev-1/push-token': { status: 204 },
    });

    expect(await registerPushToken()).toBe(true);
    expect(await registerPushToken()).toBe(true);
    expect(calls.filter((c) => c.endsWith('/push-token'))).toHaveLength(1);
  });

  it('gast ziet uitleg en een knop om in te loggen, geen inbox', async () => {
    setSessionForTest('signedOut', null);
    mockApi({});
    await renderApp(routes, '/meldingen');
    expect(await screen.findByText(/Als gast ontvang je algemene berichten/)).toBeTruthy();
    expect(screen.queryByText('Welke meldingen wil je?')).toBeNull();
  });

  it('inbox: ongelezen, gelezen markeren en de link openen; Dringend staat niet bij de voorkeuren', async () => {
    setSessionForTest('signedIn');
    const calls = mockApi({
      '/api/v1/me/notifications': inbox,
      '/api/v1/me/notification-preferences': preferences,
      '/api/v1/me/notifications/aaaaaaaa-0000-0000-0000-000000000002/read': { status: 204 },
      [`/api/v1/news/${newsId}`]: { status: 404 },
    });
    await renderApp(routes, '/meldingen');

    expect(await screen.findByText('1 ongelezen')).toBeTruthy();
    expect(screen.getByText('Optocht gaat door')).toBeTruthy();
    expect(screen.getByLabelText('Nieuws')).toBeTruthy();
    expect(screen.queryByLabelText('Dringend')).toBeNull();
    expect(screen.getByText('Pushmeldingen aanzetten')).toBeTruthy();

    fireEvent.press(screen.getByLabelText('Nieuwe prins. Lees het nieuws.'));
    expect(await screen.findByText('Nieuwsdetail')).toBeTruthy();
    expect(calls).not.toContain('/api/v1/me/notifications/aaaaaaaa-0000-0000-0000-000000000002/read');
  });

  it('zonder echt toestel geen push, wel de inbox', async () => {
    mockIsDevice = false;
    setSessionForTest('signedOut', null);
    expect(await enablePush()).toBe('unavailable');
  });
});
