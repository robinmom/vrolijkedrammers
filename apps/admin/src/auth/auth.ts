import {
  AuthError,
  CacheLookupPolicy,
  createStandardPublicClientApplication,
  type AccountInfo,
} from '@azure/msal-browser';
import type { PortalConfig } from '../config';

export interface SignedInUser {
  name: string;
  email: string;
}

/** Aanmelden bij Entra External ID; het portal kent alleen dit contract (makkelijk te mocken in tests). */
export interface AuthService {
  readonly user: SignedInUser | null;
  signIn(): Promise<void>;
  signOut(): Promise<void>;
  getAccessToken(): Promise<string>;
}

/**
 * MSAL met redirect-flow en PKCE. Tokens staan in sessionStorage (niet localStorage, fase 4-security) en verdwijnen
 * dus bij het sluiten van het tabblad.
 */
export async function createMsalAuth(config: PortalConfig): Promise<AuthService> {
  const redirectUri = `${window.location.origin}/beheer/`;
  const msal = await createStandardPublicClientApplication({
    auth: {
      clientId: config.clientId,
      authority: config.authority,
      knownAuthorities: [new URL(config.authority).host],
      redirectUri,
      postLogoutRedirectUri: redirectUri,
    },
    cache: { cacheLocation: 'sessionStorage' },
  });

  const result = await msal.handleRedirectPromise();
  const account: AccountInfo | null = result?.account ?? msal.getAllAccounts()[0] ?? null;
  if (account) {
    msal.setActiveAccount(account);
  }

  const scopes = [config.apiScope];
  return {
    user: account ? { name: account.name ?? account.username, email: account.username } : null,
    signIn: () => msal.loginRedirect({ scopes, prompt: 'select_account' }),
    signOut: () => msal.logoutRedirect(),
    async getAccessToken() {
      const active = msal.getActiveAccount();
      if (!active) {
        await msal.loginRedirect({ scopes });
        throw new Error('Aanmelden vereist');
      }
      try {
        // Alleen cache en refresh-token: stil vernieuwen via een verborgen iframe kan niet, want de portalpagina
        // mag niet in een frame (CSP frame-ancestors 'none') en dat eindigde na 10 s in "timed_out".
        return (
          await msal.acquireTokenSilent({
            scopes,
            account: active,
            cacheLookupPolicy: CacheLookupPolicy.AccessTokenAndRefreshToken,
          })
        ).accessToken;
      } catch (error) {
        // Elke MSAL-fout (verlopen refresh-token, time-out, interactie nodig): opnieuw aanmelden via redirect. Met een
        // geldige Entra-sessie gaat dat zonder nieuwe code.
        if (error instanceof AuthError) {
          await msal.acquireTokenRedirect({ scopes, account: active });
        }
        throw error;
      }
    },
  };
}

/** Alleen voor end-to-endtests: altijd aangemeld, vast token. */
export function createMockAuth(): AuthService {
  return {
    user: { name: 'Test Bestuurder', email: 'bestuur@example.com' },
    signIn: () => Promise.resolve(),
    signOut: () => Promise.resolve(),
    getAccessToken: () => Promise.resolve('e2e-token'),
  };
}
