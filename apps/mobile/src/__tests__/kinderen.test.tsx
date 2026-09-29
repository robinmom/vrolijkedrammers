import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import KindScreen from '../app/kinderen/[id]/index';
import KindQrScreen from '../app/kinderen/[id]/qr';
import MijnKinderenScreen from '../app/kinderen/index';
import KindKoppelenScreen from '../app/kinderen/koppelen';
import { setSessionForTest } from '../auth/session';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

/** Fase 17b: Mijn kinderen, kind-detail, de QR van een kind en een koppeling aanvragen. */

const routes = {
  'kinderen/index': MijnKinderenScreen,
  'kinderen/koppelen': KindKoppelenScreen,
  'kinderen/[id]/index': KindScreen,
  'kinderen/[id]/qr': KindQrScreen,
};

const LOT = '11111111-2222-3333-4444-555555555555';
const SEM = '66666666-7777-8888-9999-000000000000';

const lot = {
  memberId: LOT,
  fullName: 'Lot Mom',
  firstName: 'Lot',
  memberNumber: '1042',
  birthDate: '2016-03-10',
  age: 10,
  status: 'Active',
  ownAccount: false,
  canShowQr: true,
  groups: ['Drammerinekes'],
};

const signedInApi = {
  ...api,
  '/api/v1/me': {
    id: 'u-1',
    email: 'robin@example.com',
    displayName: 'Robin Mom',
    memberId: 'm-1',
    roles: [{ code: 'ouder', name: 'Ouder/verzorger' }],
    permissions: ['guardian.read.own', 'ticket.read.own'],
    features: {},
  },
  '/api/v1/me/children': [
    lot,
    {
      ...lot,
      memberId: SEM,
      fullName: 'Sem Mom',
      firstName: 'Sem',
      age: 16,
      ownAccount: true,
      canShowQr: false,
      groups: [],
    },
  ],
  '/api/v1/me/guardian-requests': [
    {
      id: 'r-1',
      childFirstName: 'Fenna',
      childLastName: 'Mom',
      status: 'Pending',
      createdAt: '2026-09-28T17:40:00Z',
      decidedAt: null,
    },
    {
      id: 'r-2',
      childFirstName: 'Lot',
      childLastName: 'Mom',
      status: 'Approved',
      createdAt: '2026-09-20T17:40:00Z',
      decidedAt: null,
    },
  ],
};

const requests = () =>
  (globalThis.fetch as jest.Mock).mock.calls.map(([input, init]) =>
    typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request),
  );

beforeEach(() => {
  setSessionForTest('signedIn');
  (globalThis.fetch as jest.Mock).mockClear();
});

describe('Mijn kinderen (fase 17)', () => {
  it('toont de kinderen, een eigen account en een openstaand verzoek', async () => {
    mockApi(signedInApi);
    await renderApp(routes, '/kinderen');
    expect(await screen.findByText('Lot Mom')).toBeTruthy();
    expect(screen.getByText('10 jaar · Drammerinekes')).toBeTruthy();
    expect(screen.getByText('QR beschikbaar')).toBeTruthy();
    expect(screen.getByText('Eigen account')).toBeTruthy();
    expect(screen.getByText(/Koppelverzoek voor "Fenna Mom"/)).toBeTruthy();
    expect(screen.queryByText(/"Lot Mom", verstuurd/)).toBeNull();
    expect(screen.getByRole('button', { name: 'Kind koppelen aanvragen' })).toBeTruthy();
  });

  it('kind-detail: gegevens, meldingen namens het kind, optocht en de QR-tegel', async () => {
    mockApi({
      ...signedInApi,
      [`/api/v1/me/children/${LOT}`]: {
        child: lot,
        group: 'Dansgarde',
        guardians: ['Robin Mom', 'Sanne Mom'],
        notifications: [
          {
            id: 'n-1',
            title: 'Aanwezig om 18:30 bij de zaal',
            body: 'Tot straks!',
            category: 'DanceGuard',
            deepLink: null,
            sentAt: '2026-09-29T10:04:00Z',
            readAt: null,
          },
        ],
        parade: [
          {
            paradeName: 'Optocht 2027',
            paradeDate: '2027-02-14',
            groupName: 'Dansgarde',
            startNumber: 14,
            status: 'Final',
          },
        ],
      },
    });
    await renderApp(routes, `/kinderen/${LOT}`);
    expect(await screen.findByText('Namens Lot · Aanwezig om 18:30 bij de zaal')).toBeTruthy();
    expect(screen.getByText('Robin Mom, Sanne Mom')).toBeTruthy();
    expect(screen.getByText(/startnummer 14/)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'QR van Lot. Tonen bij de deur' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Meldingen. 1 nieuw' })).toBeTruthy();
  });

  it('QR van het kind: koppelt aan de telefoon van de ouder en toont de servercode', async () => {
    const ticket = {
      state: 'Valid',
      message: 'Geldig ticket.',
      holderName: 'Lot Mom',
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
    mockApi({
      ...signedInApi,
      [`/api/v1/me/children/${LOT}/ticket`]: ticket,
      [`/api/v1/me/children/${LOT}/ticket/code`]: {
        code: 'DVD-KIND',
        issuedAt: Math.floor(Date.now() / 1000),
        validFor: 45,
      },
    });
    await renderApp(routes, `/kinderen/${LOT}/qr?naam=Lot`);
    expect(await screen.findByText('QR van Lot')).toBeTruthy();
    expect((await screen.findByTestId('qr-code')).props.children).toBe('DVD-KIND');
    expect(screen.getByText('Op jouw telefoon, als ouder/verzorger')).toBeTruthy();
    expect(requests().some((r) => r.url.endsWith('/api/v1/me/ticket'))).toBe(false);
  });

  it('QR van het kind staat op de telefoon van de andere ouder: overzetten', async () => {
    mockApi({
      ...signedInApi,
      [`/api/v1/me/children/${LOT}/ticket`]: {
        state: 'Valid',
        message: 'Geldig ticket.',
        holderName: 'Lot Mom',
        carnivalYearName: '2026/2027',
        validFrom: '2027-02-12T23:00:00Z',
        validTo: '2027-02-17T05:00:00Z',
        publicRef: 'AAECAwQFBgcICQoLDA0ODw==',
        credentialVersion: 1,
        boundToThisDevice: false,
        boundDeviceName: 'Pixel 8',
        rebindsLeft: 2,
        deviceShortId: 'AQIDBAUGBwg=',
        deviceHasHardwareKey: false,
      },
      [`/api/v1/me/children/${LOT}/ticket/bind-device`]: { status: 204 },
    });
    await renderApp(routes, `/kinderen/${LOT}/qr?naam=Lot`);
    expect(await screen.findByText('Het ticket van Lot staat op een ander toestel')).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Op dit toestel gebruiken' }));
    await waitFor(() =>
      expect(requests().some((r) => r.url.endsWith(`/api/v1/me/children/${LOT}/ticket/bind-device`))).toBe(true),
    );
  });

  it('koppeling aanvragen op naam; daarna de bevestiging', async () => {
    mockApi({
      ...signedInApi,
      '/api/v1/me/guardian-requests': (method: string) =>
        method === 'POST' ? { status: 201, body: { id: 'r-3' } } : [],
    });
    await renderApp(routes, '/kinderen/koppelen');
    const send = await screen.findByRole('button', { name: 'Verzoek versturen' });
    expect(send.props.accessibilityState.disabled).toBe(true);
    await fireEvent.changeText(screen.getByLabelText('Voornaam kind'), 'Fenna');
    await fireEvent.changeText(screen.getByLabelText('Achternaam kind'), 'Mom');
    await fireEvent.press(screen.getByRole('radio', { name: 'Verzorger' }));
    await fireEvent.press(send);
    expect(await screen.findByText('We hebben je verzoek ontvangen')).toBeTruthy();
    const post = requests().find((r) => r.url.endsWith('/api/v1/me/guardian-requests') && r.method === 'POST')!;
    expect(await post.clone().json()).toMatchObject({
      childFirstName: 'Fenna',
      childLastName: 'Mom',
      relationship: 'Caregiver',
      phone: null,
    });
  });

  it('toont de uitleg van de API als het verzoek niet kan', async () => {
    mockApi({
      ...signedInApi,
      '/api/v1/me/guardian-requests': (method: string) =>
        method === 'POST'
          ? { status: 409, body: { detail: 'Je hebt voor dit kind al een verzoek gedaan dat op het bestuur wacht.' } }
          : [],
    });
    await renderApp(routes, '/kinderen/koppelen');
    await fireEvent.changeText(await screen.findByLabelText('Voornaam kind'), 'Fenna');
    await fireEvent.changeText(screen.getByLabelText('Achternaam kind'), 'Mom');
    await fireEvent.press(screen.getByRole('button', { name: 'Verzoek versturen' }));
    expect(
      await screen.findByText('Je hebt voor dit kind al een verzoek gedaan dat op het bestuur wacht.'),
    ).toBeTruthy();
  });
});
