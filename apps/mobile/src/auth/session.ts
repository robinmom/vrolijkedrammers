import * as AuthSession from 'expo-auth-session';
import Constants from 'expo-constants';
import * as Crypto from 'expo-crypto';
import * as Device from 'expo-device';
import * as SecureStore from 'expo-secure-store';
import * as WebBrowser from 'expo-web-browser';
import { Platform } from 'react-native';
import { apiBaseUrl } from '../api/config';

/**
 * Aanmelden voor leden (fase 9, ADR-014): OIDC + PKCE via de systeembrowser met een e-mailcode van Entra External ID.
 * Het refresh-token staat in de Keychain/Keystore (alleen dit toestel); het access-token alleen in het geheugen.
 * De app kent geen "Registreren": accounts ontstaan via "Ik ben al lid" of het bestuur.
 */

export type SessionStatus = 'loading' | 'signedOut' | 'signedIn';

interface AuthConfig {
  clientId: string;
  authority: string;
  scopes: string[];
  redirectBridgeUrl: string | null;
}

const KEYS = {
  refreshToken: 'dvd.refreshToken',
  installationId: 'dvd.installationId',
  pendingSignIn: 'dvd.pendingSignIn',
} as const;

/** Een lopende aanmelding (state, PKCE-verifier, terugkeeradres), voor als Android de app opnieuw start. */
interface PendingSignIn {
  state: string;
  codeVerifier: string;
  redirectUri: string;
}

/** De inlogpagina van deze app-sessie, zolang die open is (signIn rondt een antwoord dan zelf af). */
let browserSession: Promise<unknown> | null = null;
/** Kreeg de inlogpagina van deze app-sessie het antwoord zelf (dan wisselt signIn de code in, niet de route /auth)? */
let browserGotAnswer = false;
const STORE_OPTIONS: SecureStore.SecureStoreOptions = {
  keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY,
};
/** Ververs het access-token een minuut voor het verloopt. */
const EXPIRY_MARGIN_MS = 60_000;

let status: SessionStatus = 'loading';
let accessToken: { value: string; expiresAt: number } | null = null;
let refreshing: Promise<string | null> | null = null;
let config: AuthConfig | null = null;
let discovery: AuthSession.DiscoveryDocument | null = null;
const listeners = new Set<() => void>();

function setStatus(next: SessionStatus) {
  if (status !== next) {
    status = next;
    listeners.forEach((listener) => listener());
  }
}

export function getStatus(): SessionStatus {
  return status;
}

export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** Bij het starten: ingelogd als er een refresh-token is (het access-token wordt pas bij de eerste aanroep opgehaald). */
export async function restoreSession(): Promise<void> {
  const token = await SecureStore.getItemAsync(KEYS.refreshToken, STORE_OPTIONS).catch(() => null);
  setStatus(token ? 'signedIn' : 'signedOut');
}

/** Willekeurige id van deze installatie; na afmelden of uitloggen een nieuwe. */
export async function getInstallationId(): Promise<string> {
  const existing = await SecureStore.getItemAsync(KEYS.installationId, STORE_OPTIONS);
  if (existing) {
    return existing;
  }
  const created = Crypto.randomUUID();
  await SecureStore.setItemAsync(KEYS.installationId, created, STORE_OPTIONS);
  return created;
}

async function loadConfig(): Promise<{ config: AuthConfig; discovery: AuthSession.DiscoveryDocument }> {
  if (!config) {
    const response = await fetch(`${apiBaseUrl}/api/v1/app-auth-config`);
    if (!response.ok) {
      throw new Error(`Aanmeldinstellingen niet beschikbaar (${response.status})`);
    }
    const loaded = (await response.json()) as AuthConfig;
    if (!loaded.clientId) {
      throw new Error('Inloggen is voor deze omgeving nog niet ingericht.');
    }
    config = loaded;
  }
  if (!discovery) {
    discovery = await AuthSession.fetchDiscoveryAsync(config.authority);
  }
  return { config, discovery };
}

function base64Url(value: string): string {
  // btoa werkt op Latin-1; het terugkeeradres is ASCII (exp://… of drammers://…).
  return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export type SignInResult = 'success' | 'cancelled';

/**
 * Opent de inlogpagina. In Expo Go (geen eigen URL-schema) gaat de terugkeer via de doorstuurpagina van de API
 * (alleen Dev/Acc); het terugkeeradres staat dan in de state, die de app na afloop controleert.
 */
export async function signIn(): Promise<SignInResult> {
  const { config: auth, discovery: endpoints } = await loadConfig();
  const returnUrl = AuthSession.makeRedirectUri({ scheme: 'drammers', path: 'auth' });
  const useBridge = Constants.executionEnvironment === 'storeClient' && Boolean(auth.redirectBridgeUrl);
  const redirectUri = useBridge ? auth.redirectBridgeUrl! : returnUrl;
  const nonce = Crypto.randomUUID().replace(/-/g, '');
  const request = new AuthSession.AuthRequest({
    clientId: auth.clientId,
    scopes: auth.scopes,
    redirectUri,
    usePKCE: true,
    state: `${nonce}.${base64Url(returnUrl)}`,
  });
  const url = await request.makeAuthUrlAsync(endpoints);
  // Android kan de app tijdens het inloggen afsluiten of het antwoord als gewone link openen (drammers://auth?code=…);
  // de route /auth rondt de aanmelding dan af met deze gegevens.
  const pending: PendingSignIn = { state: request.state, codeVerifier: request.codeVerifier ?? '', redirectUri };
  await SecureStore.setItemAsync(KEYS.pendingSignIn, JSON.stringify(pending), STORE_OPTIONS).catch(() => undefined);
  browserGotAnswer = false;
  const opened = WebBrowser.openAuthSessionAsync(url, returnUrl, { preferEphemeralSession: true }).then((r) => {
    browserGotAnswer = r.type === 'success';
    return r;
  });
  browserSession = opened;
  let result: WebBrowser.WebBrowserAuthSessionResult;
  try {
    result = await opened;
  } finally {
    browserSession = null;
  }
  if (result.type !== 'success') {
    // Het antwoord kan nog via de route /auth binnenkomen; die ruimt de gegevens daarna op.
    return status === 'signedIn' ? 'success' : 'cancelled';
  }
  await SecureStore.deleteItemAsync(KEYS.pendingSignIn, STORE_OPTIONS).catch(() => undefined);

  // Controleert ook de state (CSRF): een antwoord voor een ander verzoek wordt geweigerd.
  const parsed = request.parseReturnUrl(result.url);
  if (parsed.type !== 'success' || !parsed.params.code) {
    throw new Error(parsed.type === 'error' ? (parsed.error?.message ?? 'Inloggen mislukt') : 'Inloggen mislukt');
  }
  const tokens = await AuthSession.exchangeCodeAsync(
    {
      clientId: auth.clientId,
      code: parsed.params.code,
      redirectUri,
      extraParams: { code_verifier: request.codeVerifier ?? '' },
    },
    endpoints,
  );
  await storeTokens(tokens);
  await registerDevice(tokens.accessToken);
  setStatus('signedIn');
  return 'success';
}

/**
 * De inlogpagina stuurde terug naar drammers://auth?code=… terwijl signIn er niet (meer) op wachtte, bijv. omdat Android de
 * app opnieuw startte. Rondt de aanmelding af met de bewaarde PKCE-gegevens; `ignored` als signIn het zelf afhandelt of
 * als de state niet bij de bewaarde aanmelding hoort.
 */
export async function completeSignInFromRedirect(params: {
  code?: string;
  state?: string;
}): Promise<'success' | 'ignored'> {
  if (!params.code || !params.state) {
    return 'ignored';
  }
  // Loopt signIn nog: eerst afwachten. Kreeg die het antwoord zelf, dan is er al ingelogd; anders (Android gaf de link
  // alleen aan de app door) rondt deze route het af.
  if (browserSession) {
    await browserSession.catch(() => undefined);
    if (browserGotAnswer) {
      return 'ignored';
    }
  }
  if (status === 'signedIn') {
    return 'ignored';
  }
  const raw = await SecureStore.getItemAsync(KEYS.pendingSignIn, STORE_OPTIONS).catch(() => null);
  const pending = raw ? (JSON.parse(raw) as PendingSignIn) : null;
  if (!pending || pending.state !== params.state) {
    return 'ignored';
  }
  await SecureStore.deleteItemAsync(KEYS.pendingSignIn, STORE_OPTIONS).catch(() => undefined);
  const { config: auth, discovery: endpoints } = await loadConfig();
  const tokens = await AuthSession.exchangeCodeAsync(
    {
      clientId: auth.clientId,
      code: params.code,
      redirectUri: pending.redirectUri,
      extraParams: { code_verifier: pending.codeVerifier },
    },
    endpoints,
  );
  await storeTokens(tokens);
  await registerDevice(tokens.accessToken);
  setStatus('signedIn');
  return 'success';
}

async function storeTokens(tokens: AuthSession.TokenResponse) {
  accessToken = { value: tokens.accessToken, expiresAt: Date.now() + (tokens.expiresIn ?? 3600) * 1000 };
  if (tokens.refreshToken) {
    await SecureStore.setItemAsync(KEYS.refreshToken, tokens.refreshToken, STORE_OPTIONS);
  }
}

async function registerDevice(token: string) {
  const installationId = await getInstallationId();
  const response = await fetch(`${apiBaseUrl}/api/v1/me/devices`, {
    method: 'POST',
    headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json', 'x-device-id': installationId },
    body: JSON.stringify({
      installationId,
      platform: Platform.OS === 'ios' ? 'Ios' : 'Android',
      model: Device.modelName ?? null,
      appVersion: Constants.expoConfig?.version ?? null,
    }),
  });
  if (response.status === 403) {
    // Wel een geldig Entra-account, maar (nog) geen account bij de vereniging.
    await clearLocalSession();
    throw new NoAccountError();
  }
}

/** Ingelogd bij Entra, maar zonder (actief) account bij de vereniging. */
export class NoAccountError extends Error {
  constructor() {
    super('Er is (nog) geen account voor dit e-mailadres.');
  }
}

/** Geeft een geldig access-token, ververst zo nodig; `null` als er niet (meer) ingelogd is. */
export async function getAccessToken(): Promise<string | null> {
  if (status !== 'signedIn') {
    return null;
  }
  if (accessToken && accessToken.expiresAt - EXPIRY_MARGIN_MS > Date.now()) {
    return accessToken.value;
  }
  refreshing ??= refresh().finally(() => {
    refreshing = null;
  });
  return refreshing;
}

async function refresh(): Promise<string | null> {
  const refreshToken = await SecureStore.getItemAsync(KEYS.refreshToken, STORE_OPTIONS);
  if (!refreshToken) {
    await clearLocalSession();
    return null;
  }
  try {
    const { config: auth, discovery: endpoints } = await loadConfig();
    const tokens = await AuthSession.refreshAsync(
      { clientId: auth.clientId, refreshToken, scopes: auth.scopes },
      endpoints,
    );
    await storeTokens(tokens);
    return tokens.accessToken;
  } catch (error) {
    // Een geweigerd refresh-token (verlopen, account verwijderd of geblokkeerd): opnieuw inloggen. Een netwerkfout
    // laat de sessie staan, zodat de app offline blijft werken.
    if (error instanceof AuthSession.TokenError) {
      await clearLocalSession();
      return null;
    }
    throw error;
  }
}

/**
 * Wist de tokens op dit toestel. De installatie-id blijft staan, zodat opnieuw inloggen hetzelfde toestel is (munten
 * zitten vast aan het toestel van de aankoop). Alleen na afmelden van dit toestel of verwijderen van het account
 * (`newInstallation`) komt er een nieuwe id: een afgemeld toestel kan niet opnieuw aanmelden.
 */
export async function clearLocalSession({
  newInstallation = false,
}: { newInstallation?: boolean } = {}): Promise<void> {
  accessToken = null;
  await SecureStore.deleteItemAsync(KEYS.refreshToken, STORE_OPTIONS).catch(() => undefined);
  if (newInstallation) {
    await SecureStore.deleteItemAsync(KEYS.installationId, STORE_OPTIONS).catch(() => undefined);
  }
  setStatus('signedOut');
}

/** Voor tests: zet de sessie direct in een toestand zonder browser of tokenserver. */
export function setSessionForTest(next: SessionStatus, token: string | null = 'test-token') {
  accessToken = token ? { value: token, expiresAt: Date.now() + 3_600_000 } : null;
  config = null;
  discovery = null;
  setStatus(next);
}
