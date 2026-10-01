import { screen, waitFor, within } from '@testing-library/react-native';
import * as Notifications from 'expo-notifications';
import { Text } from 'react-native';
import TabLayout from '../app/(tabs)/_layout';
import HomeScreen from '../app/(tabs)/index';
import MeerScreen from '../app/(tabs)/meer';
import OptochtScreen from '../app/(tabs)/optocht';
import { setSessionForTest } from '../auth/session';
import { usePushNotifications } from '../features/usePushNotifications';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

function Schil() {
  usePushNotifications();
  return <Text>schil</Text>;
}

const routes = {
  '(tabs)/_layout': TabLayout,
  '(tabs)/index': HomeScreen,
  '(tabs)/optocht': OptochtScreen,
  '(tabs)/meer': MeerScreen,
  schil: Schil,
};

const me = {
  id: 'u-1',
  email: 'piet@example.com',
  displayName: 'Piet Lid',
  memberId: 'm-1',
  roles: [],
  permissions: ['parade.register', 'notification.read.own'],
  features: {},
};
const parade = {
  id: 'p-1',
  name: 'Optocht Loil 2027',
  paradeDate: '2027-02-07',
  startTime: '13:30:00',
  startLocation: null,
  routeDescription: null,
  routeLengthKm: null,
  registrationOpensAt: '2026-01-01T09:00:00Z',
  registrationClosesAt: '2099-01-20T23:00:00Z',
  registrationOpen: true,
  subjectRequired: true,
  maxDocumentsPerRegistration: 5,
  maxDocumentSizeMb: 10,
  infoHtml: null,
};
const signedInApi = {
  ...api,
  '/api/v1/me': me,
  '/api/v1/me/notifications': { items: [], unreadCount: 2, hasMore: false },
  '/api/v1/parade/current': parade,
  '/api/v1/parade/registrations': [
    {
      id: 'r-1',
      groupName: 'De Knotwilgen',
      status: 'AdditionalInformationRequired',
      registrationNumber: 7,
      startNumber: null,
      submittedAt: '2026-12-02T10:00:00Z',
      createdAt: '2026-12-01T10:00:00Z',
      paradeId: 'p-1',
      paradeName: 'Optocht Loil 2027',
    },
  ],
};

beforeEach(() => {
  (globalThis.fetch as jest.Mock).mockClear();
  setSessionForTest('signedIn');
});

describe('Rode telbolletjes', () => {
  it('belletje op het beginscherm toont het aantal ongelezen meldingen', async () => {
    mockApi(signedInApi);
    await renderApp(routes, '/');
    const bell = await screen.findByRole('button', { name: 'Meldingen, 2 ongelezen' });
    // Alleen binnen het belletje zoeken: elders op het beginscherm kan ook een "2" staan (aftellen, datums).
    expect(within(bell).getByText('2', { includeHiddenElements: true })).toBeTruthy();
  });

  it('Optocht: bolletje op Mijn inschrijving als er een aanvulling gevraagd is', async () => {
    mockApi(signedInApi);
    await renderApp(routes, '/optocht');
    expect(await screen.findByRole('button', { name: 'Mijn inschrijving, 1 aanvulling gevraagd' })).toBeTruthy();
  });

  it('Meer: zonder ongelezen meldingen geen bolletje', async () => {
    mockApi({ ...signedInApi, '/api/v1/me/notifications': { items: [], unreadCount: 0, hasMore: false } });
    await renderApp(routes, '/meer');
    expect((await screen.findAllByRole('button', { name: 'Meldingen' })).length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: /ongelezen/ })).toBeNull();
  });

  it('het app-icoon volgt het aantal ongelezen meldingen', async () => {
    mockApi(signedInApi);
    await renderApp(routes, '/schil');
    await waitFor(() => expect(Notifications.setBadgeCountAsync).toHaveBeenLastCalledWith(2));
  });
});
