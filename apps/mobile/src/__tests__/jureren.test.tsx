import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Alert } from 'react-native';
import OptochtScreen from '../app/(tabs)/optocht';
import BeoordelenScreen from '../app/jureren/beoordelen';
import JurerenScreen from '../app/jureren';
import UitslagenScreen from '../app/uitslagen';
import { setSessionForTest } from '../auth/session';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

/** Fase 22b: jureren in de app — eigen categorieën, sliders per voorbijtrekken, hele optocht en indienen. */

const routes = { 'jureren/index': JurerenScreen, 'jureren/beoordelen': BeoordelenScreen, '(tabs)/optocht': OptochtScreen, uitslagen: UitslagenScreen };

const me = {
  id: 'u-1',
  email: 'jury@example.com',
  displayName: 'Anke Jansen',
  memberId: null,
  roles: [],
  permissions: ['parade.judge'],
  features: {},
};

const session = (submittedAt: string | null = null) => ({
  paradeId: 'p-1',
  paradeName: 'Optocht Loil 2027',
  paradeDate: '2027-02-07',
  startTime: '13:30:00',
  submittedAt,
  categories: [{ id: 1, name: 'Getrokken wagens volwassenen' }],
  entries: [
    { registrationId: 'r-5', startNumber: 5, groupName: 'De Beunhazen', motto: 'Importheffingen Trump', categoryId: 1, categoryName: 'Getrokken wagens volwassenen', assigned: true },
    { registrationId: 'r-6', startNumber: 6, groupName: 'DwarZ', motto: 'Carnaval we’re loving IT', categoryId: 3, categoryName: 'Loopgroepen groot jeugd', assigned: false },
    { registrationId: 'r-7', startNumber: 7, groupName: 'De Snotapen', motto: 'Festa Olimpica', categoryId: 1, categoryName: 'Getrokken wagens volwassenen', assigned: true },
  ],
  scores: [] as unknown[],
});

const requests = (path: string, method: string) =>
  (globalThis.fetch as jest.Mock).mock.calls
    .map(([input, init]) => (typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request)))
    .filter((r) => r.url.includes(path) && r.method === method);

beforeEach(() => {
  (globalThis.fetch as jest.Mock).mockClear();
  setSessionForTest('signedIn');
});

describe('Jureren (fase 22b)', () => {
  it('een jurylid ziet onder Optocht de knop Jureren', async () => {
    mockApi({ ...api, '/api/v1/me': me });
    await renderApp(routes, '/optocht');
    expect(await screen.findByRole('button', { name: 'Jureren' })).toBeTruthy();
  });

  it('start: eigen categorieën en voortgang; beoordelen met sliders, opgeslagen en verstuurd', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/jury/current': session(), '/api/v1/jury/parades/p-1/scores': { changed: 1 } });
    await renderApp(routes, '/jureren');
    expect(await screen.findByText('Getrokken wagens volwassenen')).toBeTruthy();
    expect(screen.getAllByText('0 van 2')).toHaveLength(3);

    await fireEvent.press(screen.getByRole('button', { name: 'Start jureren (nr. 5)' }));
    expect(await screen.findByText('De Beunhazen')).toBeTruthy();
    expect(screen.getByText('“Importheffingen Trump”')).toBeTruthy();
    expect(screen.getByText('1 van 2')).toBeTruthy();

    const originaliteit = screen.getAllByRole('adjustable', { name: 'Originaliteit' })[0]!;
    await fireEvent(originaliteit, 'accessibilityAction', { nativeEvent: { actionName: 'increment' } });
    await fireEvent(originaliteit, 'accessibilityAction', { nativeEvent: { actionName: 'increment' } });
    await waitFor(() => expect(requests('/scores', 'PUT')).toHaveLength(1), { timeout: 4000 });
    const body = await requests('/scores', 'PUT')[0]!.json();
    expect(body.scores).toEqual([expect.objectContaining({ registrationId: 'r-5', pass: 1, criterion: 'Originality', value: 55 })]);
  });

  it('hele optocht pas na bevestiging; niet toegewezen inzendingen zijn gemarkeerd', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => buttons?.at(-1)?.onPress?.());
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/jury/current': session() });
    await renderApp(routes, '/jureren/beoordelen');
    expect(await screen.findByText('1 van 2')).toBeTruthy();
    await fireEvent.press(screen.getByRole('switch', { name: 'Hele optocht' }));
    expect(alert).toHaveBeenCalledWith('Je gaat nu de hele optocht beoordelen', expect.any(String), expect.any(Array));
    expect(await screen.findByText('1 van 3')).toBeTruthy();
    expect(screen.getByText(/Niet aan jou toegewezen/)).toBeTruthy();
    alert.mockRestore();
  });

  it('einde: indienen met bevestiging, daarna bedankt en niets meer te wijzigen', async () => {
    let submittedAt: string | null = null;
    const alert = jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => buttons?.at(-1)?.onPress?.());
    mockApi({
      ...api,
      '/api/v1/me': me,
      '/api/v1/jury/current': () => session(submittedAt),
      '/api/v1/jury/parades/p-1/submit': () => {
        submittedAt = '2027-02-07T15:42:00Z';
        return { submittedAt };
      },
    });
    await renderApp(routes, '/jureren/beoordelen?einde=1');
    expect(await screen.findByText('Klaar met jureren?')).toBeTruthy();
    expect(screen.getByText(/Voorbij 1: nr. 5, nr. 7/)).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Jurering indienen' }));
    expect(alert).toHaveBeenCalledWith('Jurering indienen?', expect.stringContaining('Daarna kun je ze niet meer wijzigen'), expect.any(Array));
    expect(await screen.findByText('Bedankt, je jurering is ingediend')).toBeTruthy();
    expect(requests('/submit', 'POST')).toHaveLength(1);
    alert.mockRestore();
  });

  it('fase 22c: de gepubliceerde uitslag staat onder Uitslagen', async () => {
    mockApi({
      ...api,
      '/api/v1/parade/results': {
        paradeName: 'Optocht Loil 2027',
        paradeDate: '2027-02-07',
        publishedAt: '2027-02-07T21:00:00Z',
        categories: [
          {
            name: 'Getrokken wagens volwassenen',
            maxPoints: 2500,
            rows: [
              { place: 1, startNumber: 64, groupName: 'De Droatneagels', motto: 'We-j goan deur tot in de 7de hemel.', total: 2204 },
              { place: 2, startNumber: 66, groupName: 'De Druktemoakers', motto: null, total: 2101.5 },
            ],
          },
        ],
      },
    });
    await renderApp(routes, '/uitslagen');
    expect(await screen.findByText('Uitslag Optocht Loil 2027')).toBeTruthy();
    expect(screen.getByLabelText('1e plaats: De Droatneagels, startnummer 64, 2.204 punten')).toBeTruthy();
    expect(screen.getByText('2.101,5')).toBeTruthy();
  });
});
