import { fireEvent, screen } from '@testing-library/react-native';
import MeerScreen from '../app/(tabs)/meer';
import TakenScreen from '../app/taken/index';
import NieuweTaakScreen from '../app/taken/nieuw';
import { setSessionForTest } from '../auth/session';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

/** Takenlijst carnaval in de app (2026-11-12): alleen eigen taken, alleen voor het kader. */

const routes = { '(tabs)/meer': MeerScreen, 'taken/index': TakenScreen, 'taken/nieuw': NieuweTaakScreen };

const me = {
  id: 'u-1',
  email: 'alfred@example.com',
  displayName: 'Alfred Kader',
  memberId: 'm-1',
  roles: [],
  permissions: ['member.read.own'],
  features: {},
};

const task = (id: string, title: string, categoryName: string, status: string, extra: Record<string, unknown> = {}) => ({
  id,
  categoryId: `c-${categoryName}`,
  categoryName,
  title,
  description: null,
  status,
  progress: status === 'Done' ? 100 : status === 'InProgress' ? 50 : 0,
  startedOn: null,
  dueOn: '2027-01-10',
  completedOn: status === 'Done' ? '2026-11-02' : null,
  overdue: false,
  note: null,
  people: null,
  ...extra,
});

const mine = {
  allowed: true,
  yearId: 2027,
  yearName: '2026/2027',
  carnivalStartDate: '2027-02-14',
  daysUntilCarnival: 94,
  summary: { total: 3, done: 1, overdue: 1, percent: 50 },
  items: [
    task('t-1', 'Uitnodigingen versturen', 'Pronkzitting', 'InProgress'),
    task('t-2', 'Wagen keuren', 'Optocht', 'NotStarted', { overdue: true, dueOn: '2026-11-01' }),
    task('t-3', 'Prijzen inkopen', 'Optocht', 'Done'),
  ],
  categories: [
    { id: 'c-Pronkzitting', name: 'Pronkzitting' },
    { id: 'c-Optocht', name: 'Optocht' },
  ],
};

const requests = (path: string, method: string) =>
  (globalThis.fetch as jest.Mock).mock.calls
    .map(([input, init]) => (typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request)))
    .filter((r) => r.url.endsWith(path) && r.method === method);

beforeEach(() => {
  (globalThis.fetch as jest.Mock).mockClear();
  setSessionForTest('signedIn');
});

describe('Takenlijst in de app', () => {
  it('Meer toont de knop voor het kader', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/me/tasks': mine });
    await renderApp(routes, '/meer');
    expect(await screen.findByRole('button', { name: 'Takenlijst' })).toBeTruthy();
  });

  it('een gewoon lid ziet de knop niet', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me,
      '/api/v1/me/tasks': { ...mine, allowed: false, items: [], categories: [], summary: { total: 0, done: 0, overdue: 0, percent: 0 } },
    });
    await renderApp(routes, '/meer');
    await screen.findByText('Alfred Kader');
    expect(screen.queryByRole('button', { name: 'Takenlijst' })).toBeNull();
  });

  it('groepeert per categorie en zet de afgeronde taken onderaan', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/me/tasks': mine });
    await renderApp(routes, '/taken');
    expect(await screen.findByText('1 van 3 taken afgerond')).toBeTruthy();
    expect(screen.getByText('Carnaval 2026/2027 · nog 94 dagen')).toBeTruthy();
    expect(screen.getByText('Pronkzitting')).toBeTruthy();
    expect(screen.getByRole('header', { name: 'Afgerond (1)' })).toBeTruthy();
    // De afgeronde taak staat er nog, zodat hij aan te passen blijft.
    expect(screen.getByText('Prijzen inkopen')).toBeTruthy();
    // Te laat valt op.
    expect(screen.getByText('Te laat')).toBeTruthy();
  });

  it('zet de voortgang van een eigen taak bij', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/me/tasks': mine, '/api/v1/me/tasks/t-1': { status: 204 } });
    await renderApp(routes, '/taken');
    await fireEvent.press(await screen.findByRole('button', { name: 'Uitnodigingen versturen, Bezig' }));
    await fireEvent.changeText(screen.getByLabelText('Opmerking'), 'Ereleden zijn verstuurd');
    await fireEvent.press(screen.getByRole('button', { name: 'Opslaan' }));
    const [put] = requests('/api/v1/me/tasks/t-1', 'PUT');
    expect(await put!.json()).toEqual({ status: 'InProgress', progress: 50, note: 'Ereleden zijn verstuurd' });
  });

  it('"niet van toepassing" haalt een taak van de lijst', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/me/tasks': mine, '/api/v1/me/tasks/t-2': { status: 204 } });
    await renderApp(routes, '/taken');
    await fireEvent.press(await screen.findByRole('button', { name: 'Wagen keuren, te laat, Nog niet gestart' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Niet van toepassing' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Opslaan' }));
    const [put] = requests('/api/v1/me/tasks/t-2', 'PUT');
    expect(await put!.json()).toEqual({ status: 'Cancelled', progress: 0, note: null });
  });

  it('voegt zelf een taak toe onder een bestaande categorie', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/me/tasks': mine });
    await renderApp(routes, '/taken/nieuw');
    await fireEvent.changeText(await screen.findByLabelText('Taak'), 'Kaartverkoop regelen');
    await fireEvent.press(screen.getByRole('button', { name: 'Optocht' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Voor carnaval' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Taak toevoegen' }));
    const [post] = requests('/api/v1/me/tasks', 'POST');
    expect(await post!.json()).toEqual({
      categoryId: 'c-Optocht',
      title: 'Kaartverkoop regelen',
      description: null,
      // De dag voor carnaval; de app heeft geen datumkiezer, dus dit zijn snelkeuzes.
      dueOn: '2027-02-13',
      status: 'NotStarted',
      progress: 0,
    });
  });
});
