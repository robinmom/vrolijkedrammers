// Inloggen op de website (fase 21d): dezelfde Entra External ID-login als de app en het portal, via MSAL (redirect + PKCE).
// Tokens staan in sessionStorage en verdwijnen bij het sluiten van het tabblad. Gebruik: await DrammersLogin.init(), daarna
// DrammersLogin.account, signIn(), signOut() en fetch(url, options) met het toegangstoken.
window.DrammersLogin = (() => {
  'use strict';
  let app = null;
  let scopes = [];
  const state = { available: false, account: null };

  async function init(redirectPath) {
    try {
      if (!window.msal) return state;
      const response = await fetch('/api/v1/portal-config');
      if (!response.ok) return state;
      const config = await response.json();
      if (!config.clientId || !config.authority || !config.apiScope) return state;
      const redirectUri = `${window.location.origin}${redirectPath}`;
      app = await window.msal.createStandardPublicClientApplication({
        auth: {
          clientId: config.clientId,
          authority: config.authority,
          knownAuthorities: [new URL(config.authority).host],
          redirectUri,
          postLogoutRedirectUri: redirectUri,
        },
        cache: { cacheLocation: 'sessionStorage' },
      });
      scopes = [config.apiScope];
      const result = await app.handleRedirectPromise();
      state.account = result?.account ?? app.getAllAccounts()[0] ?? null;
      if (state.account) app.setActiveAccount(state.account);
      state.available = true;
    } catch {
      // Inloggen niet beschikbaar (bijv. geblokkeerde opslag): de pagina werkt dan zonder account.
      state.available = false;
    }
    return state;
  }

  async function token() {
    const account = app?.getActiveAccount();
    if (!account) throw new Error('Niet ingelogd');
    try {
      // Alleen cache en refresh-token: stil vernieuwen in een verborgen frame kan niet (frame-ancestors 'none').
      return (await app.acquireTokenSilent({ scopes, account, cacheLookupPolicy: window.msal.CacheLookupPolicy.AccessTokenAndRefreshToken })).accessToken;
    } catch (error) {
      await app.acquireTokenRedirect({ scopes, account });
      throw error;
    }
  }

  return {
    state,
    init,
    signIn: () => app.loginRedirect({ scopes, prompt: 'select_account' }),
    signOut: () => app.logoutRedirect({ account: app.getActiveAccount() }),
    async fetch(url, options = {}) {
      const headers = { ...(options.headers ?? {}), authorization: `Bearer ${await token()}` };
      return fetch(url, { ...options, headers });
    },
  };
})();
