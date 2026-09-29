import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { p256 } from '@noble/curves/nist.js';
import * as Haptics from 'expo-haptics';
import MeerScreen from '../app/(tabs)/meer';
import ScannenScreen from '../app/scannen';
import { setSessionForTest } from '../auth/session';
import { base45, signedPayload, toBase64, unsignedPayload } from '../features/deviceQr';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

const routes = { scannen: ScannenScreen, '(tabs)/meer': MeerScreen };

const me = (permissions: string[]) => ({
  id: 'u-1',
  email: 'deur@example.com',
  displayName: 'Marieke Deur',
  memberId: null,
  roles: [],
  permissions,
  features: {},
});
const current = { id: 'ev-1', title: 'Carnavalsavond', startAt: '2027-02-13T19:00:00Z', endAt: '2027-02-14T01:00:00Z' };
const counts = { inside: 1, scans: 1, refused: 0 };
const result = (outcome: string, title: string, message: string, needsDecision = false) => ({
  scanId: 's-1',
  outcome,
  title,
  message,
  holderName: 'Piet van der Lid',
  previousAt: null,
  needsDecision,
  counts,
});

const posts = (path: string) =>
  (globalThis.fetch as jest.Mock).mock.calls
    .map(([input, init]) => (typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request)))
    .filter((r) => r.url.endsWith(path) && r.method === 'POST');

beforeEach(() => {
  (globalThis.fetch as jest.Mock).mockClear();
  setSessionForTest('signedIn');
});

describe('Scannen bij de deur (fase 14b)', () => {
  it('zonder de rol Deurcontrole: geen scanner en geen knop onder Meer', async () => {
    mockApi({ ...api, '/api/v1/me': me(['member.read.own']) });
    await renderApp(routes, '/scannen');
    expect(await screen.findByText('Alleen voor deurcontrole')).toBeTruthy();
  });

  it('Meer toont de knop voor deurcontrole', async () => {
    mockApi({ ...api, '/api/v1/me': me(['ticket.scan']) });
    await renderApp(routes, '/meer');
    expect(await screen.findByRole('button', { name: 'Scannen bij de deur' })).toBeTruthy();
  });

  it('buiten een activiteit met toegangscontrole: de volgende activiteit', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me(['ticket.scan']),
      '/api/v1/access/status': { current: null, next: current, counts: null },
    });
    await renderApp(routes, '/scannen');
    expect(await screen.findByText('Geen toegangscontrole nu')).toBeTruthy();
    expect(screen.getByText(/Volgende: .* – Carnavalsavond/)).toBeTruthy();
  });

  it('groen met naam en trillen; daarna volgende scannen', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me(['ticket.scan']),
      '/api/v1/access/status': { current, next: null, counts: { inside: 0, scans: 0, refused: 0 } },
      '/api/v1/access/scan': result('Admitted', 'Toegang geldig', 'Eerste keer vanavond'),
    });
    await renderApp(routes, '/scannen');
    expect(await screen.findByText(/Carnavalsavond ·/)).toBeTruthy();
    await fireEvent.press(screen.getByTestId('camera'));
    expect(await screen.findByText('Toegang geldig')).toBeTruthy();
    expect(screen.getByText('Piet van der Lid')).toBeTruthy();
    expect(Haptics.notificationAsync).toHaveBeenLastCalledWith('success');
    await fireEvent.press(screen.getByRole('button', { name: 'Volgende scannen' }));
    expect(await screen.findByLabelText('1 binnen, 1 scans, 0 geweigerd')).toBeTruthy();
  });

  it('oranje: toch toelaten wordt bewaard', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me(['ticket.scan']),
      '/api/v1/access/status': { current, next: null, counts },
      '/api/v1/access/scan': result('Warning', 'Let op', 'Vanavond al gescand op een ander toestel om 20:58.', true),
      '/api/v1/access/scans/s-1/decision': { inside: 1, scans: 2, refused: 0 },
    });
    await renderApp(routes, '/scannen');
    await fireEvent.press(await screen.findByTestId('camera'));
    expect(await screen.findByText('Let op')).toBeTruthy();
    expect(Haptics.notificationAsync).toHaveBeenLastCalledWith('warning');
    await fireEvent.press(screen.getByRole('button', { name: 'Toch toelaten' }));
    await waitFor(() => expect(posts('/api/v1/access/scans/s-1/decision')).toHaveLength(1));
    expect(await posts('/api/v1/access/scans/s-1/decision')[0]!.clone().json()).toEqual({ admit: true });
  });

  it('rood met de reden', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me(['ticket.scan']),
      '/api/v1/access/status': { current, next: null, counts },
      '/api/v1/access/scan': result('Refused', 'Geen toegang', 'Ticket geblokkeerd.'),
    });
    await renderApp(routes, '/scannen');
    await fireEvent.press(await screen.findByTestId('camera'));
    expect(await screen.findByText('Geen toegang')).toBeTruthy();
    expect(screen.getByText('Ticket geblokkeerd.')).toBeTruthy();
    expect(Haptics.notificationAsync).toHaveBeenLastCalledWith('error');
  });

  it('offline: geen antwoord van de server → gecontroleerd met de controlelijst en in de wachtrij', async () => {
    await AsyncStorage.clear();
    const key = p256.keygen();
    const header = '3059301306072a8648ce3d020106082a8648ce3d030107034200'.match(/../g)!.map((h) => parseInt(h, 16));
    const ref = Uint8Array.from({ length: 16 }, (_, i) => i);
    const deviceId = Uint8Array.from([1, 2, 3, 4, 5, 6, 7, 8]);
    const unsigned = unsignedPayload({
      ref,
      credentialVersion: 1,
      deviceId,
      issuedAt: Math.floor(Date.now() / 1000),
      validFor: 45,
    });
    (globalThis as { __qr?: string }).__qr = base45(signedPayload(unsigned, p256.sign(unsigned, key.secretKey)));
    mockApi({
      ...api,
      '/api/v1/me': me(['ticket.scan']),
      '/api/v1/access/status': { current, next: null, counts },
      '/api/v1/access/offline-pack': {
        generatedAt: new Date().toISOString(),
        current,
        validFrom: new Date(Date.now() - 3_600_000).toISOString(),
        validTo: new Date(Date.now() + 3_600_000).toISOString(),
        serverKeys: [],
        tickets: [
          {
            ref: toBase64(ref),
            credentialVersion: 1,
            blocked: false,
            membershipActive: true,
            deviceShortId: toBase64(deviceId),
            devicePublicKey: toBase64(Uint8Array.from([...header, ...p256.getPublicKey(key.secretKey, false)])),
            holderName: 'Piet van der Lid',
          },
        ],
      },
      '/api/v1/access/scan': () => {
        throw new TypeError('Network request failed');
      },
    });
    await renderApp(routes, '/scannen');
    await waitFor(() =>
      expect(
        (globalThis.fetch as jest.Mock).mock.calls.some(([r]) =>
          String((r as Request).url ?? r).includes('offline-pack'),
        ),
      ).toBe(true),
    );
    await fireEvent.press(await screen.findByTestId('camera'));
    expect(await screen.findByText('Offline gecontroleerd')).toBeTruthy();
    expect(screen.getByText('Piet van der Lid')).toBeTruthy();
    expect(JSON.parse((await AsyncStorage.getItem('dvd.offline-scans.v1'))!)).toHaveLength(1);
    await fireEvent.press(screen.getByRole('button', { name: 'Volgende scannen' }));
    expect(await screen.findByText('1 offline scan wacht op verzending')).toBeTruthy();
    delete (globalThis as { __qr?: string }).__qr;
  });
});
