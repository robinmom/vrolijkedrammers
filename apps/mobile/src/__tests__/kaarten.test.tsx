import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Alert } from 'react-native';
import * as WebBrowser from 'expo-web-browser';
import AfrekenenScreen from '../app/kaarten/afrekenen';
import BestellingScreen from '../app/kaarten/bestelling';
import DelenScreen from '../app/kaarten/delen';
import KaartenScreen from '../app/kaarten/index';
import MijnKaartenScreen from '../app/kaarten/mijn';
import PronkzittingScreen from '../app/kaarten/pronkzitting';
import MuntenScreen from '../app/munten/index';
import MuntenKopenScreen from '../app/munten/kopen';
import { setSessionForTest } from '../auth/session';
import { QR_VERSION_TOKENS, unsignedPayload } from '../features/deviceQr';
import { routeForLink } from '../features/push';
import { loadGuestOrders, tokensToCollect } from '../features/sales';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

/** Fase 19b: kaarten kopen, pronkzitting (vol → wachtlijst), afrekenen, Mijn kaarten, delen en de munten-QR. */

const routes = {
  'kaarten/index': KaartenScreen,
  'kaarten/pronkzitting': PronkzittingScreen,
  'kaarten/afrekenen': AfrekenenScreen,
  'kaarten/bestelling': BestellingScreen,
  'kaarten/mijn': MijnKaartenScreen,
  'kaarten/delen': DelenScreen,
  'munten/index': MuntenScreen,
  'munten/kopen': MuntenKopenScreen,
};

const product = (id: string, kind: string, name: string, extra: Record<string, unknown> = {}) => ({
  id,
  kind,
  name,
  description: null,
  eventId: null,
  date: null,
  priceCents: 1250,
  capacity: 300,
  remaining: 42,
  soldOut: false,
  maxPerOrder: 10,
  saleClosesAt: null,
  membersOnly: kind === 'Tokens',
  groupOrders: kind === 'Pronkzitting',
  ...extra,
});

const vrijdag = product('11111111-0000-0000-0000-000000000001', 'Pronkzitting', 'Pronkzitting vrijdag', {
  date: '2027-02-05',
  remaining: 0,
  soldOut: true,
});
const zaterdag = product('11111111-0000-0000-0000-000000000002', 'Pronkzitting', 'Pronkzitting zaterdag', { date: '2027-02-06' });
const dagkaart = product('11111111-0000-0000-0000-000000000003', 'DayTicket', 'Dagkaart zaterdag', { date: '2027-02-13', priceCents: 750 });
const munten = product('11111111-0000-0000-0000-000000000004', 'Tokens', 'Consumptiemunten', { priceCents: 250, capacity: null, remaining: null, maxPerOrder: 100 });

const catalog = (isMember: boolean) => ({
  products: [vrijdag, zaterdag, dagkaart, munten],
  isMember,
  group: isMember ? { groupName: 'Kruumels', activeMembers: 13, ordered: 8, remaining: 5 } : null,
});

const order = (extra: Record<string, unknown> = {}) => ({
  id: '22222222-0000-0000-0000-000000000001',
  number: '2027-0142',
  status: 'Confirmed',
  kind: 'Pronkzitting',
  productName: 'Pronkzitting zaterdag',
  date: '2027-02-06',
  groupName: 'Kruumels',
  memberQuantity: 7,
  paidQuantity: 0,
  amountCents: 0,
  buyerName: 'Mendy Mom',
  createdAt: '2026-10-12T18:00:00Z',
  holdUntil: null,
  tickets: [{ id: '33333333-0000-0000-0000-000000000001', quantity: 6, status: 'Active', code: 'DVD-GROEP', canShare: true }],
  sharedBy: null,
  sharedWith: [{ name: 'Ruby', quantity: 1 }],
  ...extra,
});

const me = {
  id: 'u-1',
  email: 'mendy@example.com',
  displayName: 'Mendy Mom',
  memberId: 'm-1',
  roles: [{ code: 'lid', name: 'Carnavalist' }],
  permissions: ['ticket.read.own'],
  features: {},
};

const requests = () =>
  (globalThis.fetch as jest.Mock).mock.calls.map(([input, init]) =>
    typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request),
  );

beforeEach(() => {
  (globalThis.fetch as jest.Mock).mockClear();
  (WebBrowser.openAuthSessionAsync as jest.Mock).mockClear();
});

describe('Kaarten (fase 19b)', () => {
  it('gast: overzicht zonder munten en met Mijn kaarten', async () => {
    setSessionForTest('signedOut', null);
    mockApi({ ...api, '/api/v1/sales/products': catalog(false) });
    await renderApp(routes, '/kaarten');
    expect(await screen.findByText('Pronkzitting')).toBeTruthy();
    expect(screen.getByText('Dagkaart zaterdag')).toBeTruthy();
    expect(screen.queryByText('Consumptiemunten')).toBeNull();
    expect(screen.getByText(/Munten zijn alleen voor leden/)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Mijn kaarten' })).toBeTruthy();
  });

  it('pronkzitting als lid: groepskaarten, vrijdag vol → zaterdag of de wachtlijst', async () => {
    setSessionForTest('signedIn');
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/sales/products': catalog(true) });
    await renderApp(routes, '/kaarten/pronkzitting');
    expect(await screen.findByText('Voor de Kruumels')).toBeTruthy();
    expect(screen.getByText(/13 leden · 8 al besteld door de groep/)).toBeTruthy();
    // Standaard de avond met plaats (zaterdag); kies vrijdag (vol).
    await fireEvent.press(screen.getByRole('radio', { name: /vrijdag 5 februari, vol/ }));
    await fireEvent.press(screen.getByTestId('Kaarten groep-plus'));
    await fireEvent.press(screen.getByTestId('Kaarten groep-plus'));
    expect(screen.getByText('Totaal: 2 kaarten')).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Verder naar afrekenen' }));
    expect(alert).toHaveBeenCalledWith(
      'Vrijdag 5 februari is vol',
      expect.stringContaining('zaterdag 6 februari'),
      expect.arrayContaining([expect.objectContaining({ text: 'Op de wachtlijst' })]),
    );
    alert.mockRestore();
  });

  it('afrekenen als gast: iDEAL via Mollie, bestelling op dit toestel onthouden', async () => {
    setSessionForTest('signedOut', null);
    mockApi({
      ...api,
      '/api/v1/sales/products': catalog(false),
      '/api/v1/sales/orders': {
        status: 201,
        body: { orderId: order().id, number: '2027-0142', token: 'geheim', status: 'AwaitingPayment', checkoutUrl: 'https://mollie.test/pay' },
      },
      [`/api/v1/sales/orders/${order().id}`]: order({ status: 'AwaitingPayment', tickets: [], sharedWith: [] }),
    });
    await renderApp(routes, `/kaarten/afrekenen?product=${dagkaart.id}&paid=2`);
    expect(await screen.findByText('Ben je lid?', { exact: false })).toBeTruthy();
    expect(screen.getAllByText('€ 15,00', { exact: false }).length).toBeGreaterThan(0);
    await fireEvent.changeText(screen.getByLabelText('Naam'), 'Jan Jansen');
    await fireEvent.changeText(screen.getByLabelText('E-mailadres'), 'jan@example.com');
    await fireEvent.press(screen.getByRole('button', { name: 'Betalen met iDEAL · € 15,00' }));
    await waitFor(() => expect(WebBrowser.openAuthSessionAsync).toHaveBeenCalledWith('https://mollie.test/pay', 'drammers://kaarten/bestelling'));
    const post = requests().find((r) => r.url.endsWith('/api/v1/sales/orders') && r.method === 'POST')!;
    expect(await post.clone().json()).toMatchObject({ productId: dagkaart.id, paidQuantity: 2, memberQuantity: 0, buyerName: 'Jan Jansen', channel: 'App' });
    expect(await loadGuestOrders()).toContainEqual({ id: order().id, token: 'geheim' });
    expect(await screen.findByText('We wachten op de betaling. Dat duurt meestal een paar seconden.')).toBeTruthy();
  });

  it('Mijn kaarten: één groeps-QR, gedeeld met Ruby, en delen met een groepslid', async () => {
    setSessionForTest('signedIn');
    const ticketId = order().tickets[0]!.id;
    mockApi({
      ...api,
      '/api/v1/me': me,
      '/api/v1/me/orders': [order()],
      [`/api/v1/me/orders/tickets/${ticketId}/share-candidates`]: [
        { memberId: 'm-2', name: 'Ruby Mom', hasAccount: true, hasTicket: false },
        { memberId: 'm-3', name: 'Sanne de Vries', hasAccount: false, hasTicket: false },
      ],
      [`/api/v1/me/orders/tickets/${ticketId}/share`]: { status: 204 },
    });
    await renderApp(routes, '/kaarten/mijn');
    expect(await screen.findByText('Geldig voor 6 personen · 1 gedeeld met Ruby')).toBeTruthy();
    expect(screen.getByTestId('qr-code').props.children).toBe('DVD-GROEP');
    await fireEvent.press(screen.getByRole('button', { name: 'Kaart delen met groepslid' }));
    expect(await screen.findByRole('radio', { name: 'Sanne de Vries, heeft de app niet' })).toBeDisabled();
    await fireEvent.press(screen.getByRole('radio', { name: 'Ruby Mom' }));
    expect(screen.getByText('Jij houdt 5 kaarten over')).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Delen met Ruby' }));
    await waitFor(() => expect(requests().some((r) => r.url.endsWith(`/tickets/${ticketId}/share`) && r.method === 'POST')).toBe(true));
    const share = requests().find((r) => r.url.endsWith(`/tickets/${ticketId}/share`))!;
    expect(await share.clone().json()).toEqual({ memberId: 'm-2', quantity: 1 });
  });

  it('munten kopen: alleen voor leden', async () => {
    setSessionForTest('signedOut', null);
    mockApi({ ...api, '/api/v1/sales/products': catalog(false) });
    await renderApp(routes, '/munten/kopen');
    expect(await screen.findByText('Log in als lid om munten te kopen.')).toBeTruthy();
  });

  it('munten: met betaalde munten de munten-QR, ook vóór carnaval', async () => {
    setSessionForTest('signedIn');
    const tokens = order({ id: 't-1', kind: 'Tokens', productName: 'Consumptiemunten', groupName: null, paidQuantity: 20, memberQuantity: 0, sharedWith: [], tickets: [{ id: 't', quantity: 20, status: 'Active', code: null, canShare: false }] });
    expect(tokensToCollect([tokens as never])).toBe(20);
    mockApi({
      ...api,
      '/api/v1/me': me,
      '/api/v1/me/orders': [tokens],
      '/api/v1/me/ticket': {
        state: 'NotYetValid',
        message: '',
        holderName: 'Mendy Mom',
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
      },
      '/api/v1/me/ticket/code': { code: 'DVD-MUNTEN', issuedAt: Math.floor(Date.now() / 1000), validFor: 45 },
    });
    await renderApp(routes, '/munten');
    // Ook vóór carnaval (bijvoorbeeld op de pronkzitting) staat de munten-QR klaar.
    expect(await screen.findByText('MUNTEN · PERSOONSGEBONDEN')).toBeTruthy();
    expect(screen.getByText('20 munten betaald · nog af te halen')).toBeTruthy();
    expect((await screen.findByTestId('qr-code')).props.children).toBe('DVD-MUNTEN');
    expect(screen.getByText('Niet te delen: alleen jij kunt afhalen')).toBeTruthy();
    expect(requests().find((r) => r.url.includes('/api/v1/me/ticket/code'))!.url).toContain('purpose=Tokens');
  });

  it('munten-QR op het toestel is versie 4; meldingen openen Mijn kaarten en Munten', () => {
    const bytes = unsignedPayload({
      ref: new Uint8Array(16),
      credentialVersion: 1,
      deviceId: new Uint8Array(8),
      issuedAt: 1,
      validFor: 45,
      version: QR_VERSION_TOKENS,
    });
    expect(bytes[0]).toBe(4);
    expect(routeForLink('drammers://kaarten')).toBe('/kaarten/mijn');
    expect(routeForLink('drammers://kaarten/mijn')).toBe('/kaarten/mijn');
    expect(routeForLink('drammers://munten')).toBe('/munten');
  });
});
