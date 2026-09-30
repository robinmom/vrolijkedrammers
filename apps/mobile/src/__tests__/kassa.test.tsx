import { fireEvent, screen } from '@testing-library/react-native';
import MeerScreen from '../app/(tabs)/meer';
import KassaScreen from '../app/kassa';
import ScannenScreen from '../app/scannen';
import { setSessionForTest } from '../auth/session';
import { base45 } from '../features/deviceQr';
import { checkOffline } from '../features/offlineScan';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

/** Fase 19c: de Kassa (munten uitgeven) en gekochte kaarten bij de deur. */

const routes = { kassa: KassaScreen, scannen: ScannenScreen, '(tabs)/meer': MeerScreen };

const me = (permissions: string[]) => ({
  id: 'u-1',
  email: 'kassa@example.com',
  displayName: 'Kees Kassa',
  memberId: null,
  roles: [],
  permissions,
  features: {},
});

const ready = {
  scanId: 'ks-1',
  outcome: 'Ready',
  title: 'Munten uitgeven',
  message: 'Persoonsgebonden · nog niet afgehaald',
  holderName: 'Mendy Mom',
  quantity: 20,
  orderNumber: '2027-0311',
  paidWith: 'iDEAL',
};

const posts = (path: string) =>
  (globalThis.fetch as jest.Mock).mock.calls
    .map(([input, init]) => (typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request)))
    .filter((r) => r.url.endsWith(path) && r.method === 'POST');

beforeEach(() => {
  (globalThis.fetch as jest.Mock).mockClear();
  setSessionForTest('signedIn');
});

describe('Kassa (fase 19c)', () => {
  it('zonder de rol Kassa: geen kassa en geen knop onder Meer', async () => {
    mockApi({ ...api, '/api/v1/me': me(['ticket.scan']) });
    await renderApp(routes, '/kassa');
    expect(await screen.findByText('Alleen voor de kassa')).toBeTruthy();
  });

  it('Meer toont de knop voor de kassa', async () => {
    mockApi({ ...api, '/api/v1/me': me(['sale.collect']) });
    await renderApp(routes, '/meer');
    expect(await screen.findByRole('button', { name: 'Kassa: munten uitgeven' })).toBeTruthy();
  });

  it('scan toont de bestelling; pas na "Bestelling uitgegeven" is hij uitgegeven', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me(['sale.collect']),
      '/api/v1/kassa/scan': ready,
      '/api/v1/kassa/scans/ks-1/issue': { ...ready, outcome: 'Issued', title: 'Uitgegeven', message: '20 munten uitgegeven om 21:14.' },
    });
    await renderApp(routes, '/kassa');
    await fireEvent.press(await screen.findByTestId('camera'));
    expect(await screen.findByText('Munten uitgeven')).toBeTruthy();
    expect(screen.getByText('Mendy Mom')).toBeTruthy();
    expect(screen.getByText('20 munten')).toBeTruthy();
    expect(screen.getByText('Betaald met iDEAL · bestelling 2027-0311')).toBeTruthy();
    expect(posts('/api/v1/kassa/scans/ks-1/issue')).toHaveLength(0);
    await fireEvent.press(screen.getByRole('button', { name: 'Bestelling uitgegeven' }));
    expect(await screen.findByText('Uitgegeven')).toBeTruthy();
    expect(screen.getByText('20 munten uitgegeven om 21:14.')).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Volgende scannen' }));
    expect(await screen.findByTestId('camera')).toBeTruthy();
  });

  it('geweigerd met de reden, zonder uitgeefknop', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me(['sale.collect']),
      '/api/v1/kassa/scan': {
        ...ready,
        scanId: null,
        outcome: 'Refused',
        title: 'Niet uitgeven',
        message: 'Deze munten zijn al uitgegeven om 20:57 (bestelling 2027-0311).',
      },
    });
    await renderApp(routes, '/kassa');
    await fireEvent.press(await screen.findByTestId('camera'));
    expect(await screen.findByText('Niet uitgeven')).toBeTruthy();
    expect(screen.getByText('Deze munten zijn al uitgegeven om 20:57 (bestelling 2027-0311).')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Bestelling uitgegeven' })).toBeNull();
  });
});

describe('Gekochte kaarten bij de deur (fase 19c)', () => {
  const current = { id: 'ev-1', title: 'Pronkzitting vrijdag', startAt: '2027-02-05T19:00:00Z', endAt: '2027-02-06T01:00:00Z' };

  it('groen met het aantal personen dat tegelijk naar binnen gaat', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me(['ticket.scan']),
      '/api/v1/access/status': { current, next: null, counts: { inside: 0, scans: 0, refused: 0 } },
      '/api/v1/access/scan': {
        scanId: 's-1',
        outcome: 'Admitted',
        title: 'Toegang geldig',
        message: '6 personen tegelijk naar binnen · Pronkzitting vrijdag',
        holderName: 'Kruumels',
        previousAt: null,
        needsDecision: false,
        counts: { inside: 6, scans: 1, refused: 0 },
        persons: 6,
      },
    });
    await renderApp(routes, '/scannen');
    await fireEvent.press(await screen.findByTestId('camera'));
    expect(await screen.findByText('6 personen')).toBeTruthy();
    expect(screen.getByText('Kruumels')).toBeTruthy();
  });

  it('offline: een gekochte kaart of munten-QR wordt niet goedgekeurd', () => {
    const bytes = (version: number) => {
      const b = new Uint8Array(97);
      b[0] = version;
      return base45(b);
    };
    const pack = { generatedAt: '', current: null, validFrom: null, validTo: null, serverKeys: [], tickets: [] };
    expect(checkOffline(pack as never, bytes(3), new Date(), new Map()).message).toMatch(/alleen online/);
    expect(checkOffline(pack as never, bytes(4), new Date(), new Map()).message).toMatch(/munten-QR/);
    expect(checkOffline(pack as never, bytes(3), new Date(), new Map()).outcome).toBe('Refused');
  });
});

