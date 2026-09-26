import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as AuthSession from 'expo-auth-session';
import * as SecureStore from 'expo-secure-store';
import * as WebBrowser from 'expo-web-browser';
import { Alert } from 'react-native';
import TabLayout from '../app/(tabs)/_layout';
import MeerScreen from '../app/(tabs)/meer';
import MijnGegevensScreen from '../app/account/index';
import ApparatenScreen from '../app/account/apparaten';
import AccountVerwijderenScreen from '../app/account/verwijderen';
import AccountAanvragenScreen from '../app/meer/account-aanvragen';
import InloggenScreen from '../app/meer/inloggen';
import LidWordenScreen from '../app/meer/lid-worden';
import { shouldPersistQuery } from '../api/QueryProvider';
import { api as client } from '../api/client';
import { getStatus, NoAccountError, restoreSession, setSessionForTest, signIn } from '../auth/session';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

jest.mock('expo-auth-session', () => {
  const actual = jest.requireActual('expo-auth-session');
  return {
    ...actual,
    fetchDiscoveryAsync: jest.fn(async () => ({ authorizationEndpoint: 'https://login.example/authorize', tokenEndpoint: 'https://login.example/token' })),
    makeRedirectUri: jest.fn(() => 'exp://192.168.1.10:8081/--/auth'),
    exchangeCodeAsync: jest.fn(async () => ({ accessToken: 'access-1', refreshToken: 'refresh-1', expiresIn: 3600 })),
    refreshAsync: jest.fn(),
    AuthRequest: jest.fn().mockImplementation((config: { state: string; redirectUri: string }) => ({
      config,
      codeVerifier: 'verifier-123',
      makeAuthUrlAsync: jest.fn(async () => `https://login.example/authorize?state=${config.state}`),
      parseReturnUrl: jest.fn((url: string) =>
        url.includes(`state=${config.state}`) ? { type: 'success', params: { code: 'code-1' } } : { type: 'error', error: { message: 'state' } },
      ),
    })),
  };
});
jest.mock('expo-constants', () => {
  const actual = jest.requireActual('expo-constants');
  return { ...actual, __esModule: true, default: { ...actual.default, executionEnvironment: 'storeClient', expoConfig: { version: '1.0.0' } } };
});

const routes = {
  '(tabs)/_layout': TabLayout,
  '(tabs)/meer': MeerScreen,
  'meer/inloggen': InloggenScreen,
  'meer/account-aanvragen': AccountAanvragenScreen,
  'meer/lid-worden': LidWordenScreen,
  'account/index': MijnGegevensScreen,
  'account/apparaten': ApparatenScreen,
  'account/verwijderen': AccountVerwijderenScreen,
};

const member = {
  memberNumber: '0101',
  fullName: 'Piet van der Lid',
  firstName: 'Piet',
  addressLine: 'Dorpsstraat 1',
  postalCode: '6999 AA',
  city: 'Loil',
  email: 'piet@example.com',
  phone: null,
  birthDate: '1980-03-12',
  joinYear: 1995,
  status: 'Active',
  membershipValidTo: null,
  groups: [{ name: 'Jeugdcommissie', function: 'Lead' }],
};
const me = {
  id: 'u-1',
  email: 'piet@example.com',
  displayName: 'Piet van der Lid',
  memberId: 'm-1',
  roles: [{ code: 'lid', name: 'Carnavalist' }],
  permissions: ['member.read.own'],
  features: {},
};
const devices = [
  { id: 'd-1', name: 'iPhone 15', platform: 'Ios', model: 'iPhone 15', appVersion: '1.0.0', status: 'Active', createdAt: '2026-09-20T10:00:00Z', lastSeenAt: '2026-09-26T10:00:00Z', current: true },
  { id: 'd-2', name: 'Pixel 8', platform: 'Android', model: 'Pixel 8', appVersion: '1.0.0', status: 'Active', createdAt: '2026-09-21T10:00:00Z', lastSeenAt: '2026-09-25T10:00:00Z', current: false },
];
const signedInApi = { ...api, '/api/v1/me': me, '/api/v1/me/member': { status: 200, body: member }, '/api/v1/me/devices': devices };

const store = (SecureStore as unknown as { __store: Map<string, string> }).__store;
const requests = () =>
  (globalThis.fetch as jest.Mock).mock.calls.map(([input, init]) => (typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request)));

beforeEach(() => {
  store.clear();
  setSessionForTest('signedOut', null);
  (globalThis.fetch as jest.Mock).mockClear();
});

describe('Meer en inloggen', () => {
  it('niet ingelogd: inloggen voor leden, zonder "Registreren"', async () => {
    mockApi(api);
    await renderApp(routes, '/meer');
    await fireEvent.press(await screen.findByLabelText(/Inloggen voor leden/));
    expect(await screen.findByText('Voor leden')).toBeTruthy();
    expect(screen.queryByText(/Registreren/)).toBeNull();
    expect(screen.getByText('Account aanvragen')).toBeTruthy();
  });

  it('ingelogd: Mijn gegevens met naam in plaats van Lid worden', async () => {
    setSessionForTest('signedIn');
    mockApi(signedInApi);
    await renderApp(routes, '/meer');
    expect(await screen.findByText('Piet van der Lid')).toBeTruthy();
    expect(screen.queryByText('Word ook een Drammer!')).toBeNull();
  });
});

describe('Account aanvragen', () => {
  it('stuurt lidnummer en e-mail en toont de algemene melding', async () => {
    mockApi({ ...api, '/api/v1/account-requests': { status: 202, body: { message: 'Bedankt! Klopt alles, dan ontvang je een e-mail.' } } });
    await renderApp(routes, '/meer/account-aanvragen');
    const button = await screen.findByRole('button', { name: 'Account aanvragen' });
    expect(button.props.accessibilityState.disabled).toBe(true);

    await fireEvent.changeText(screen.getByLabelText('Lidnummer'), ' 0101 ');
    await fireEvent.changeText(screen.getByLabelText('E-mailadres'), 'piet@example.com');
    await fireEvent.press(button);

    expect(await screen.findByText('Bedankt! Klopt alles, dan ontvang je een e-mail.')).toBeTruthy();
    const post = requests().find((r) => r.url.endsWith('/api/v1/account-requests'))!;
    expect(await post.clone().json()).toEqual({ memberNumber: '0101', email: 'piet@example.com' });
    expect(post.headers.get('authorization')).toBeNull();
  });

  it('te veel pogingen: duidelijke melding', async () => {
    mockApi({ ...api, '/api/v1/account-requests': { status: 429 } });
    await renderApp(routes, '/meer/account-aanvragen');
    await fireEvent.changeText(await screen.findByLabelText('Lidnummer'), '0101');
    await fireEvent.changeText(screen.getByLabelText('E-mailadres'), 'piet@example.com');
    await fireEvent.press(screen.getByRole('button', { name: 'Account aanvragen' }));
    expect(await screen.findByText(/Te veel aanvragen/)).toBeTruthy();
  });
});

describe('Mijn gegevens', () => {
  it('toont de eigen gegevens met token en apparaat-id, en de hint voor wijzigingen', async () => {
    setSessionForTest('signedIn');
    mockApi(signedInApi);
    await renderApp(routes, '/account');
    expect(await screen.findByText('Piet van der Lid')).toBeTruthy();
    expect(screen.getByText('Dorpsstraat 1, 6999 AA Loil')).toBeTruthy();
    expect(screen.getByText('12 maart 1980')).toBeTruthy();
    expect(screen.getByText('Jeugdcommissie · Leiding')).toBeTruthy();
    expect(screen.getByText(/secretariaat/)).toBeTruthy();

    const call = requests().find((r) => r.url.endsWith('/api/v1/me/member'))!;
    expect(call.headers.get('authorization')).toBe('Bearer test-token');
    expect(call.headers.get('x-device-id')).toMatch(/^[0-9a-f-]{36}$/);
  });

  it('niet ingelogd: door naar inloggen', async () => {
    setSessionForTest('signedOut', null);
    mockApi(api);
    await renderApp(routes, '/account');
    expect(await screen.findByText('Voor leden')).toBeTruthy();
  });

  it('gegevens downloaden opent de exportlink', async () => {
    setSessionForTest('signedIn');
    mockApi({ ...signedInApi, '/api/v1/me/privacy/export': { id: 'e-1', expiresAt: '2026-09-27T10:00:00Z', downloadUrl: 'https://blob.example/export.json?sas' } });
    await renderApp(routes, '/account');
    await fireEvent.press(await screen.findByText('Mijn gegevens downloaden'));
    await waitFor(() => expect(WebBrowser.openBrowserAsync).toHaveBeenCalledWith('https://blob.example/export.json?sas'));
  });

  it('een afgemeld apparaat (401) logt uit', async () => {
    setSessionForTest('signedIn');
    mockApi({ ...signedInApi, '/api/v1/me/member': { status: 401, body: { code: 'DEVICE_REVOKED' } } });
    await renderApp(routes, '/account');
    await waitFor(() => expect(getStatus()).toBe('signedOut'));
    expect(await screen.findByText('Voor leden')).toBeTruthy();
  });
});

describe('Apparaten en account verwijderen', () => {
  it('een ander apparaat afmelden na bevestiging', async () => {
    setSessionForTest('signedIn');
    mockApi({ ...signedInApi, '/api/v1/me/devices/d-2': { status: 204 } });
    const alert = jest.spyOn(Alert, 'alert');
    await renderApp(routes, '/account/apparaten');
    expect(await screen.findByText('iPhone 15 (dit apparaat)')).toBeTruthy();
    // Alleen het andere apparaat heeft een knop "Afmelden".
    await fireEvent.press(screen.getByRole('button', { name: 'Afmelden' }));
    const buttons = alert.mock.calls[0]![2]!;
    await buttons.find((b) => b.text === 'Afmelden')!.onPress!();
    expect(requests().some((r) => r.method === 'DELETE' && r.url.endsWith('/api/v1/me/devices/d-2'))).toBe(true);
  });

  it('account verwijderen vraagt de bevestiging en logt daarna uit', async () => {
    setSessionForTest('signedIn');
    store.set('dvd.refreshToken', 'refresh-1');
    mockApi({ ...signedInApi, '/api/v1/me': { status: 204 } });
    await renderApp(routes, '/account/verwijderen');
    const button = await screen.findByRole('button', { name: 'Account verwijderen' });
    expect(button.props.accessibilityState.disabled).toBe(true);
    await fireEvent.changeText(screen.getByLabelText('Typ VERWIJDEREN om te bevestigen'), 'VERWIJDEREN');
    await fireEvent.press(button);

    await waitFor(() => expect(getStatus()).toBe('signedOut'));
    const del = requests().find((r) => r.method === 'DELETE' && r.url.endsWith('/api/v1/me'))!;
    expect(await del.clone().json()).toEqual({ confirmation: 'VERWIJDEREN' });
    expect(store.has('dvd.refreshToken')).toBe(false);
  });
});

describe('Sessie', () => {
  const authConfig = { clientId: 'app-client', authority: 'https://login.example/v2.0', scopes: ['openid', 'offline_access', 'api://x/access_as_user'], redirectBridgeUrl: 'https://api.example/app/auth-redirect' };

  it('Expo Go: inloggen via de doorstuurpagina met PKCE, tokens in de Keychain en apparaat aangemeld', async () => {
    mockApi({ '/api/v1/app-auth-config': authConfig, '/api/v1/me/devices': { status: 200, body: devices[0] } });
    (WebBrowser.openAuthSessionAsync as jest.Mock).mockImplementationOnce(async (url: string) => ({
      type: 'success',
      url: `exp://192.168.1.10:8081/--/auth?code=code-1&state=${new URL(url).searchParams.get('state')}`,
    }));

    await expect(signIn()).resolves.toBe('success');

    const config = (AuthSession.AuthRequest as unknown as jest.Mock).mock.calls.at(-1)![0];
    expect(config.redirectUri).toBe('https://api.example/app/auth-redirect');
    expect(atob(config.state.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'))).toBe('exp://192.168.1.10:8081/--/auth');
    expect(AuthSession.exchangeCodeAsync).toHaveBeenCalledWith(
      expect.objectContaining({ code: 'code-1', redirectUri: 'https://api.example/app/auth-redirect', extraParams: { code_verifier: 'verifier-123' } }),
      expect.anything(),
    );
    expect(store.get('dvd.refreshToken')).toBe('refresh-1');
    expect(getStatus()).toBe('signedIn');
    const register = requests().find((r) => r.url.endsWith('/api/v1/me/devices'))!;
    expect(register.headers.get('authorization')).toBe('Bearer access-1');
  });

  it('geen account bij de vereniging: sessie gewist en een duidelijke fout', async () => {
    mockApi({ '/api/v1/app-auth-config': authConfig, '/api/v1/me/devices': { status: 403 } });
    (WebBrowser.openAuthSessionAsync as jest.Mock).mockImplementationOnce(async (url: string) => ({
      type: 'success',
      url: `exp://x/--/auth?code=code-1&state=${new URL(url).searchParams.get('state')}`,
    }));
    await expect(signIn()).rejects.toBeInstanceOf(NoAccountError);
    expect(getStatus()).toBe('signedOut');
    expect(store.has('dvd.refreshToken')).toBe(false);
  });

  it('annuleren in de browser verandert niets', async () => {
    mockApi({ '/api/v1/app-auth-config': authConfig });
    await expect(signIn()).resolves.toBe('cancelled');
    expect(getStatus()).toBe('signedOut');
  });

  it('bij het starten ingelogd als er een refresh-token is; publieke aanroepen zonder token als uitgelogd', async () => {
    store.set('dvd.refreshToken', 'refresh-1');
    await restoreSession();
    expect(getStatus()).toBe('signedIn');

    setSessionForTest('signedOut', null);
    mockApi(api);
    await client.GET('/api/v1/news');
    expect(requests().at(-1)!.headers.get('authorization')).toBeNull();
  });

  it('persoonlijke gegevens komen niet in de offline cache', () => {
    const query = (key: string[]) => ({ queryKey: key, state: { status: 'success' } }) as never;
    expect(shouldPersistQuery(query(['news']))).toBe(true);
    expect(shouldPersistQuery(query(['me', 'member']))).toBe(false);
  });
});
