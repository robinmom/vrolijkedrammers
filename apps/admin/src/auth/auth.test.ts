import { AuthError, CacheLookupPolicy } from '@azure/msal-browser';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createMsalAuth } from './auth';

const account = { name: 'Robin', username: 'robin@example.com', homeAccountId: 'h', environment: 'e', tenantId: 't', localAccountId: 'l' };
const msal = {
  handleRedirectPromise: vi.fn(),
  getAllAccounts: vi.fn(),
  setActiveAccount: vi.fn(),
  getActiveAccount: vi.fn(),
  acquireTokenSilent: vi.fn(),
  acquireTokenRedirect: vi.fn(),
  loginRedirect: vi.fn(),
  logoutRedirect: vi.fn(),
};

vi.mock('@azure/msal-browser', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@azure/msal-browser')>()),
  createStandardPublicClientApplication: () => Promise.resolve(msal),
}));

const config = { clientId: 'c', authority: 'https://tenant.ciamlogin.com/t/v2.0', apiScope: 'api://x/.default' };

describe('createMsalAuth', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    msal.handleRedirectPromise.mockResolvedValue(null);
    msal.getAllAccounts.mockReturnValue([account]);
    msal.getActiveAccount.mockReturnValue(account);
  });

  it('vernieuwt stil alleen via cache en refresh-token (geen iframe: de CSP verbiedt framen)', async () => {
    msal.acquireTokenSilent.mockResolvedValue({ accessToken: 'token-1' });
    const auth = await createMsalAuth(config as never);

    await expect(auth.getAccessToken()).resolves.toBe('token-1');
    expect(msal.acquireTokenSilent).toHaveBeenCalledWith(
      expect.objectContaining({ cacheLookupPolicy: CacheLookupPolicy.AccessTokenAndRefreshToken }),
    );
  });

  it('stuurt bij een time-out door naar aanmelden in plaats van de fout te tonen', async () => {
    msal.acquireTokenSilent.mockRejectedValue(new AuthError('timed_out', 'iframe'));
    const auth = await createMsalAuth(config as never);

    await expect(auth.getAccessToken()).rejects.toThrow();
    expect(msal.acquireTokenRedirect).toHaveBeenCalledWith(expect.objectContaining({ account }));
  });
});
