import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as Brightness from 'expo-brightness';
import MijnQrScreen from '../app/mijn-qr';
import { setSessionForTest } from '../auth/session';
import { validityText } from '../features/ticket';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

const routes = { 'mijn-qr': MijnQrScreen };

const ticket = {
  state: 'Valid',
  message: 'Geldig ticket.',
  holderName: 'Piet van der Lid',
  carnivalYearName: '2026/2027',
  validFrom: '2027-02-12T23:00:00Z',
  validTo: '2027-02-17T05:00:00Z',
  publicRef: 'AAECAwQFBgcICQoLDA0ODw==',
  credentialVersion: 1,
  boundToThisDevice: true,
  boundDeviceName: 'iPhone 15',
  rebindsLeft: 3,
  deviceShortId: 'AQIDBAUGBwg=',
  deviceHasHardwareKey: false,
};

const methodOf = (path: string) =>
  (globalThis.fetch as jest.Mock).mock.calls
    .map(([input, init]) => (typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request)))
    .filter((r) => r.url.endsWith(path))
    .map((r) => r.method);

beforeEach(() => (globalThis.fetch as jest.Mock).mockClear());

describe('Mijn QR (fase 13b)', () => {
  it('geldigheid als "za 13 t/m di 16 februari 2027"', () => {
    expect(validityText(ticket.validFrom, ticket.validTo)).toBe('za 13 t/m di 16 februari 2027');
  });

  it('gast: eerst inloggen', async () => {
    setSessionForTest('signedOut', null);
    mockApi(api);
    await renderApp(routes, '/mijn-qr');
    expect(await screen.findByText('Mijn QR is voor leden')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Inloggen' })).toBeTruthy();
  });

  it('geldig ticket zonder hardwaresleutel: servercode als QR, live en maximale helderheid', async () => {
    setSessionForTest('signedIn');
    mockApi({
      ...api,
      '/api/v1/me/ticket': ticket,
      '/api/v1/me/ticket/code': { code: 'DVD-TESTCODE', issuedAt: Math.floor(Date.now() / 1000), validFor: 45 },
    });
    await renderApp(routes, '/mijn-qr');
    expect(await screen.findByText('Piet van der Lid')).toBeTruthy();
    expect((await screen.findByTestId('qr-code')).props.children).toBe('DVD-TESTCODE');
    expect(await screen.findByText(/Live · ververst over \d+ s/)).toBeTruthy();
    expect(screen.getByText('Code van de server: internet nodig')).toBeTruthy();
    expect(Brightness.setBrightnessAsync).toHaveBeenCalledWith(1);
  });

  it('vóór carnaval: koppelt automatisch aan dit toestel en toont wanneer de QR verschijnt', async () => {
    setSessionForTest('signedIn');
    mockApi({
      ...api,
      '/api/v1/me/ticket': { ...ticket, state: 'NotYetValid', boundToThisDevice: false, boundDeviceName: null },
      '/api/v1/me/ticket/bind-device': { status: 204 },
    });
    await renderApp(routes, '/mijn-qr');
    expect(await screen.findByText('Je QR verschijnt bij carnaval')).toBeTruthy();
    await waitFor(() => expect(methodOf('/api/v1/me/ticket/bind-device')).toEqual(['POST']));
    expect(screen.getByRole('button', { name: 'Zet in mijn agenda' })).toBeTruthy();
  });

  it('op een ander toestel: overzetten naar dit toestel', async () => {
    setSessionForTest('signedIn');
    mockApi({
      ...api,
      '/api/v1/me/ticket': { ...ticket, boundToThisDevice: false, boundDeviceName: 'Pixel 8', rebindsLeft: 2 },
      '/api/v1/me/ticket/bind-device': { status: 204 },
    });
    await renderApp(routes, '/mijn-qr');
    expect(await screen.findByText('Je ticket staat op een ander toestel')).toBeTruthy();
    expect(screen.getByText('Nog 2 keer overzetten mogelijk dit carnavalsjaar')).toBeTruthy();
    expect(methodOf('/api/v1/me/ticket/bind-device')).toEqual([]);
    await fireEvent.press(screen.getByRole('button', { name: 'Op dit toestel gebruiken' }));
    await waitFor(() => expect(methodOf('/api/v1/me/ticket/bind-device')).toEqual(['POST']));
  });

  it('geen actief lidmaatschap of geblokkeerd: geen QR', async () => {
    setSessionForTest('signedIn');
    mockApi({
      ...api,
      '/api/v1/me/ticket': {
        ...ticket,
        state: 'Blocked',
        message: 'Je ticket is geblokkeerd. Neem contact op met het bestuur.',
        publicRef: null,
      },
    });
    await renderApp(routes, '/mijn-qr');
    expect(await screen.findByText('Geen geldig ticket')).toBeTruthy();
    expect(screen.getByText('Je ticket is geblokkeerd. Neem contact op met het bestuur.')).toBeTruthy();
    expect(screen.queryByTestId('qr-code')).toBeNull();
  });
});
