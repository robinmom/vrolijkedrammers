import { fireEvent, screen } from '@testing-library/react-native';
import MeerScreen from '../app/(tabs)/meer';
import AdverteerdersScreen from '../app/adverteerders/index';
import NieuweAdverteerderScreen from '../app/adverteerders/nieuw';
import { setSessionForTest } from '../auth/session';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

/** Fase 27b-2: de collectant haalt adverteerders op in de app. */

const routes = { '(tabs)/meer': MeerScreen, 'adverteerders/index': AdverteerdersScreen, 'adverteerders/nieuw': NieuweAdverteerderScreen };

const me = {
  id: 'u-1',
  email: 'alfred@example.com',
  displayName: 'Alfred Kader',
  memberId: 'm-1',
  roles: [],
  permissions: ['member.read.own'],
  features: {},
};

const advertiser = (id: string, companyName: string, status: string, previousAmount: number | null, amount: number | null = null) => ({
  id,
  number: Number(id.slice(-1)),
  companyName,
  contactName: 'Jan Test',
  phone: '0314-000000',
  mobile: null,
  email: null,
  addressLine: 'Dorpsstraat 1',
  postalCode: '6941 XX',
  city: 'Loil',
  kind: 'Advertisement',
  payment: 'Mandate',
  status,
  amount,
  previousAmount,
  note: null,
  page: '4',
  cashReceived: false,
  history: previousAmount === null ? [] : [{ year: 2026, amount: previousAmount, isFree: false, status: 'Collected' }],
});

const mine = {
  isCollector: true,
  year: 2027,
  items: [advertiser('a-1', 'Bakkerij De Test', 'Open', 35), advertiser('a-2', 'Garage Proef', 'Collected', 70, 70)],
};

const requests = (path: string, method: string) =>
  (globalThis.fetch as jest.Mock).mock.calls
    .map(([input, init]) => (typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request)))
    .filter((r) => r.url.endsWith(path) && r.method === method);

beforeEach(() => {
  (globalThis.fetch as jest.Mock).mockClear();
  setSessionForTest('signedIn');
});

describe('Adverteerders ophalen (fase 27b-2)', () => {
  it('Meer toont de knop alleen voor een kaderlid', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/me/advertisers': mine });
    await renderApp(routes, '/meer');
    expect(await screen.findByRole('button', { name: 'Adverteerders ophalen' })).toBeTruthy();
  });

  it('geen kaderlid: geen knop', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/me/advertisers': { isCollector: false, year: 2027, items: [] } });
    await renderApp(routes, '/meer');
    await screen.findByText('Alfred Kader');
    expect(screen.queryByRole('button', { name: 'Adverteerders ophalen' })).toBeNull();
  });

  it('toont de voortgang en vinkt een adverteerder af met het bedrag van vorig jaar', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/me/advertisers': mine, '/api/v1/me/advertisers/a-1/status': { status: 204 } });
    await renderApp(routes, '/adverteerders');
    expect(await screen.findByText('1 van 2 afgehandeld')).toBeTruthy();
    // Standaard alleen wie nog bezocht moet worden.
    expect(screen.queryByText('Garage Proef')).toBeNull();

    await fireEvent.press(screen.getByRole('button', { name: 'Bakkerij De Test, Nog langs' }));
    expect(screen.getByDisplayValue('35')).toBeTruthy();
    // Wat ze eerder gaven, per carnavalsjaar.
    expect(screen.getByText('2025/2026')).toBeTruthy();
    expect(screen.getByLabelText('Eerdere jaren')).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Afvinken als opgehaald' }));
    const [put] = requests('/api/v1/me/advertisers/a-1/status', 'PUT');
    expect(await put!.json()).toEqual({ status: 'Collected', amount: 35, note: null, cashReceived: null });
  });

  it('bij contant vinkt de collectant aan dat het geld is ontvangen', async () => {
    const cash = { ...advertiser('a-3', 'Kapsalon Contant', 'Open', 50), payment: 'Cash' };
    mockApi({
      ...api,
      '/api/v1/me': me,
      '/api/v1/me/advertisers': { ...mine, items: [cash] },
      '/api/v1/me/advertisers/a-3/status': { status: 204 },
    });
    await renderApp(routes, '/adverteerders');
    await fireEvent.press(await screen.findByRole('button', { name: 'Kapsalon Contant, Nog langs' }));
    await fireEvent.press(screen.getByRole('checkbox', { name: 'Geld contant ontvangen' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Afvinken als opgehaald' }));
    const [put] = requests('/api/v1/me/advertisers/a-3/status', 'PUT');
    expect(await put!.json()).toEqual({ status: 'Collected', amount: 50, note: null, cashReceived: true });
  });

  it('toont het paginanummer en slaat een opmerking op zonder de stand te wijzigen (fase 27g)', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me,
      '/api/v1/me/advertisers': mine,
      '/api/v1/me/advertisers/a-1/note': { status: 204 },
      '/api/v1/me/advertisers/a-1/status': { status: 204 },
    });
    await renderApp(routes, '/adverteerders');
    expect(await screen.findByText('Pagina 4 · Jan Test · Loil')).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Bakkerij De Test, Nog langs' }));
    await fireEvent.changeText(screen.getByLabelText('Opmerking'), 'Volgend jaar een halve pagina');
    await fireEvent.press(screen.getByRole('button', { name: 'Opmerking opslaan' }));
    expect(await screen.findByRole('button', { name: 'Opmerking opgeslagen' })).toBeTruthy();
    const [note] = requests('/api/v1/me/advertisers/a-1/note', 'PUT');
    expect(await note!.json()).toEqual({ note: 'Volgend jaar een halve pagina' });
    expect(requests('/api/v1/me/advertisers/a-1/status', 'PUT')).toHaveLength(0);

    // Afvinken neemt de opmerking mee.
    await fireEvent.press(screen.getByRole('button', { name: 'Afvinken als opgehaald' }));
    const [put] = requests('/api/v1/me/advertisers/a-1/status', 'PUT');
    expect(await put!.json()).toEqual({ status: 'Collected', amount: 35, note: 'Volgend jaar een halve pagina', cashReceived: null });
  });

  it('toont de contactgegevens in het overzicht en de informatie van de campagne (fase 27h)', async () => {
    const withContact = { ...advertiser('a-1', 'Bakkerij De Test', 'Open', 35), mobile: '06 12345678', email: 'info@bakkerij.test' };
    mockApi({
      ...api,
      '/api/v1/me': me,
      '/api/v1/me/advertisers': { ...mine, items: [withContact], info: 'Advertenties: 85/50 € 35,00\nInleveren: 1 december' },
    });
    await renderApp(routes, '/adverteerders');
    await screen.findByText('Bakkerij De Test');
    // Zonder openklappen: telefoon en e-mail aan te tikken.
    expect(screen.getByRole('link', { name: 'Bel 06 12345678' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Bel 0314-000000' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Mail info@bakkerij.test' })).toBeTruthy();

    expect(screen.queryByText(/Inleveren/)).toBeNull();
    await fireEvent.press(screen.getByRole('button', { name: 'Info' }));
    expect(screen.getByText('Advertenties: 85/50 € 35,00\nInleveren: 1 december')).toBeTruthy();
  });

  it('nieuwe adverteerder met machtiging vraagt IBAN en toestemming', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/me/advertisers': (method: string) => (method === 'POST' ? { status: 201, body: { id: 'a-9' } } : mine) });
    await renderApp(routes, '/adverteerders/nieuw');
    await fireEvent.changeText(await screen.findByLabelText('Naam bedrijf'), 'Nieuwe Zaak');
    await fireEvent.changeText(screen.getByLabelText('Bedrag (€)'), '50');
    expect(screen.getByRole('button', { name: 'Adverteerder toevoegen' })).toBeDisabled();
    await fireEvent.changeText(screen.getByLabelText('IBAN'), 'NL91ABNA0417164300');
    await fireEvent.press(screen.getByRole('checkbox'));
    await fireEvent.press(screen.getByRole('button', { name: 'Adverteerder toevoegen' }));
    expect(await screen.findByText(/Nieuwe Zaak staat op opgehaald voor 2026\/2027/)).toBeTruthy();
    const [post] = requests('/api/v1/me/advertisers', 'POST');
    expect(await post!.json()).toMatchObject({ companyName: 'Nieuwe Zaak', amount: 50, payment: 'Mandate', iban: 'NL91ABNA0417164300', mandateConsent: true });
  });
});
