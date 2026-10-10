import { screen, waitFor } from '@testing-library/react-native';
import * as AuthSession from 'expo-auth-session';
import * as SecureStore from 'expo-secure-store';
import HomeScreen from '../app/(tabs)/index';
import AuthReturnScreen from '../app/auth';
import { getStatus, setSessionForTest } from '../auth/session';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

jest.mock('expo-auth-session', () => ({
  ...jest.requireActual('expo-auth-session'),
  fetchDiscoveryAsync: jest.fn(),
  exchangeCodeAsync: jest.fn(),
}));

/** Android: de inlogpagina stuurt terug naar drammers://auth?code=… als gewone link (geen "Unmatched Route" meer). */

const routes = { '(tabs)/index': HomeScreen, auth: AuthReturnScreen };

describe('Terugkeer van de inlogpagina', () => {
  beforeEach(() => setSessionForTest('signedOut', null));

  it('zonder bewaarde aanmelding: terug naar de app, niet ingelogd', async () => {
    mockApi(api);
    await renderApp(routes, '/auth?code=abc&state=onbekend');
    await waitFor(() => expect(screen.queryByText('Bezig met inloggen…')).toBeNull());
    expect(getStatus()).toBe('signedOut');
  });

  it('na een herstart van de app: rondt de aanmelding af met de bewaarde gegevens', async () => {
    const store = new Map<string, string>([
      ['dvd.pendingSignIn', JSON.stringify({ state: 's-1', codeVerifier: 'v-1', redirectUri: 'drammers://auth' })],
    ]);
    jest.spyOn(SecureStore, 'getItemAsync').mockImplementation(async (key) => store.get(key) ?? null);
    jest.spyOn(SecureStore, 'setItemAsync').mockImplementation(async (key, value) => void store.set(key, value));
    jest.spyOn(SecureStore, 'deleteItemAsync').mockImplementation(async (key) => void store.delete(key));
    jest.mocked(AuthSession.fetchDiscoveryAsync).mockResolvedValue({ tokenEndpoint: 'https://login/token' });
    const exchange = jest
      .mocked(AuthSession.exchangeCodeAsync)
      .mockResolvedValue({ accessToken: 'at', refreshToken: 'rt', expiresIn: 3600 } as AuthSession.TokenResponse);
    mockApi({
      ...api,
      '/api/v1/app-auth-config': { clientId: 'c', authority: 'https://login', scopes: ['openid'], redirectBridgeUrl: null },
      '/api/v1/me/devices': { status: 204 },
    });

    await renderApp(routes, '/auth?code=abc&state=s-1');
    await waitFor(() => expect(getStatus()).toBe('signedIn'));
    expect(exchange).toHaveBeenCalledWith(
      expect.objectContaining({ code: 'abc', redirectUri: 'drammers://auth', extraParams: { code_verifier: 'v-1' } }),
      expect.anything(),
    );
    expect(store.has('dvd.pendingSignIn')).toBe(false);
    expect(store.get('dvd.refreshToken')).toBe('rt');
  });
});
