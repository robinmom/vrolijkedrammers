import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Alert } from 'react-native';
import TabLayout from '../app/(tabs)/_layout';
import HomeScreen from '../app/(tabs)/index';
import MeerScreen from '../app/(tabs)/meer';
import FestivalBeoordelenScreen from '../app/dansgardefestival-jury/beoordelen';
import HeadJuryScreen from '../app/dansgardefestival-jury/hoofdjury';
import FestivalJuryScreen from '../app/dansgardefestival-jury';
import { setSessionForTest } from '../auth/session';
import { api } from '../test/api-fixture';
import { mockApi, PHONE_SIZE, renderApp, setWindowSize, TABLET_SIZE } from '../test/render';

/** 2026-10-15: jureren op het Dansgardefestival — alleen de categorie die aan de beurt is, 0 tot 10, indienen, hoofdjury. */

const routes = {
  'dansgardefestival-jury/index': FestivalJuryScreen,
  'dansgardefestival-jury/beoordelen': FestivalBeoordelenScreen,
  'dansgardefestival-jury/hoofdjury': HeadJuryScreen,
  '(tabs)/meer': MeerScreen,
};

const me = (permissions: string[], extra: { memberId?: string | null; roles?: { code: string; name: string }[] } = {}) => ({
  id: 'u-1',
  email: 'merel@example.com',
  displayName: 'Merel',
  memberId: extra.memberId ?? null,
  roles: extra.roles ?? [],
  permissions,
  features: {},
});

const juryRole = [{ code: 'jury-dansgardefestival', name: 'Jury dansgardefestival' }];

const category = (key: string, name: string, solo: boolean, dances: number, submitted: boolean, current: boolean) => ({
  key,
  name,
  solo,
  dances,
  start: '10:35',
  submitted,
  current,
});

const session = (submitted = false) => ({
  festivalId: 'f-1',
  festivalName: 'Dansgardefestival 2027',
  festivalDate: '2027-02-21',
  location: 'Hart van Loil',
  jurorNumber: 2,
  headJury: true,
  categories: [
    category('Minioren|Garde|Group|', 'Minioren Garde', false, 2, submitted, !submitted),
    category('Hoofdklasse|Garde|Solo|', 'Hoofdklasse Solo Garde', true, 1, false, submitted),
  ],
  current: submitted
    ? category('Hoofdklasse|Garde|Solo|', 'Hoofdklasse Solo Garde', true, 1, false, true)
    : category('Minioren|Garde|Group|', 'Minioren Garde', false, 2, false, true),
  dances: submitted
    ? [{ entryId: 'e-3', number: 1, club: 'CV De Deurdouwers', names: 'Lynn Knuman', title: null, dancerCount: 1, averageAge: 16 }]
    : [
        { entryId: 'e-1', number: 1, club: 'CV De Vrolijke Drammers', names: 'Mini Drammers', title: null, dancerCount: 8, averageAge: 7.5 },
        { entryId: 'e-2', number: 2, club: 'DG Dynamite', names: 'Flames', title: null, dancerCount: 9, averageAge: 7 },
      ],
  scores: [] as unknown[],
  comments: [] as unknown[],
});

const requests = (path: string, method: string) =>
  (globalThis.fetch as jest.Mock).mock.calls
    .map(([input, init]) => (typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request)))
    .filter((r) => r.url.includes(path) && r.method === method);

beforeEach(() => {
  (globalThis.fetch as jest.Mock).mockClear();
  setSessionForTest('signedIn');
});

describe('Jureren dansgardefestival', () => {
  it('een jurylid ziet onder Meer de knop en alleen de categorie die aan de beurt is', async () => {
    mockApi({ ...api, '/api/v1/me': me(['festival.judge', 'festival.head-jury']), '/api/v1/festival-jury': session() });
    await renderApp(routes, '/meer');
    await fireEvent.press(await screen.findByRole('button', { name: 'Jureren dansgardefestival' }));
    expect(await screen.findByText('Jurylid 2')).toBeTruthy();
    expect(screen.getAllByText('Minioren Garde').length).toBeGreaterThan(0);
    expect(screen.getByText('Daarna: Hoofdklasse Solo Garde')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Hoofdjury: uitslag' })).toBeTruthy();
  });

  it('beoordelen: vier onderdelen in halve punten, totaal en opmerking worden verstuurd', async () => {
    mockApi({ ...api, '/api/v1/me': me(['festival.judge']), '/api/v1/festival-jury': session(), '/api/v1/festival-jury/scores': {} });
    await renderApp(routes, '/dansgardefestival-jury/beoordelen');
    expect(await screen.findByText('Dans 1 van 2')).toBeTruthy();
    expect(screen.getAllByRole('adjustable', { name: 'Synchroniteit' }).length).toBeGreaterThan(0);
    const execution = screen.getAllByRole('adjustable', { name: 'Uitvoering' })[0]!;
    await fireEvent(execution, 'accessibilityAction', { nativeEvent: { actionName: 'increment' } });
    await fireEvent(execution, 'accessibilityAction', { nativeEvent: { actionName: 'increment' } });
    await fireEvent.changeText(screen.getAllByLabelText('Opmerking voor de vereniging')[0]!, 'Mooie lijnen');
    await waitFor(() => expect(requests('/festival-jury/scores', 'PUT').length).toBeGreaterThan(0), { timeout: 4000 });
    const bodies = await Promise.all(requests('/festival-jury/scores', 'PUT').map((r) => r.json()));
    // Twee keer vegen vanaf het midden: halve punten, dus 5,5 en niet 7 (2026-11-02).
    expect(bodies.flatMap((b) => b.scores)).toContainEqual(expect.objectContaining({ entryId: 'e-1', criterion: 'Execution', value: 5.5 }));
    expect(screen.getAllByText('5,5').length).toBeGreaterThan(0);
    expect(bodies.flatMap((b) => b.comments)).toContainEqual({ entryId: 'e-1', text: 'Mooie lijnen' });
  });

  it('tablet: de danslijst staat ernaast en springt naar een andere dans', async () => {
    setWindowSize(TABLET_SIZE);
    mockApi({ ...api, '/api/v1/me': me(['festival.judge']), '/api/v1/festival-jury': session(), '/api/v1/festival-jury/scores': {} });
    await renderApp(routes, '/dansgardefestival-jury/beoordelen');
    expect(await screen.findByText('Dans 1 van 2')).toBeTruthy();
    expect(screen.getByText('2 dansen · 0 beoordeeld')).toBeTruthy();
    await fireEvent.press(screen.getByLabelText('2, Flames, nog niet beoordeeld'));
    expect(await screen.findByText('Dans 2 van 2')).toBeTruthy();
    setWindowSize(PHONE_SIZE);
  });

  it('solo: Ritme in plaats van Synchroniteit; indienen pas als alles punten heeft', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => buttons?.at(-1)?.onPress?.());
    mockApi({
      ...api,
      '/api/v1/me': me(['festival.judge']),
      '/api/v1/festival-jury': {
        ...session(true),
        scores: ['Synchronicity', 'Choreography', 'Execution', 'Difficulty'].map((criterion) => ({ entryId: 'e-3', criterion, value: 9, scoredAt: '2027-02-21T12:00:00Z' })),
      },
      '/api/v1/festival-jury/submit': {},
    });
    await renderApp(routes, '/dansgardefestival-jury/beoordelen?dans=1');
    expect(await screen.findByText('Klaar met Hoofdklasse Solo Garde?')).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Categorie indienen' }));
    expect(alert).toHaveBeenCalledWith('Hoofdklasse Solo Garde indienen?', expect.any(String), expect.any(Array));
    await waitFor(() => expect(requests('/festival-jury/submit', 'POST')).toHaveLength(1));
    expect(await requests('/festival-jury/submit', 'POST')[0]!.json()).toEqual({ categoryKey: 'Hoofdklasse|Garde|Solo|' });
    alert.mockRestore();
  });

  it('hoofdjury: van de laatste plaats naar de winnaar en de prijsuitreiking afronden', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => buttons?.at(-1)?.onPress?.());
    const row = (place: number, names: string, total: number) => ({
      entryId: names,
      registrationId: 'r',
      place,
      club: 'CV',
      names,
      title: null,
      synchronicity: 0,
      choreography: 0,
      execution: 0,
      difficulty: 0,
      total,
      perJuror: [],
    });
    mockApi({
      ...api,
      '/api/v1/me': me(['festival.head-jury']),
      '/api/v1/festival-jury/head': {
        published: true,
        festivalName: 'Dansgardefestival 2027',
        awardsCompletedAt: null,
        parts: [
          {
            part: 1,
            name: 'Deel 1 · Minioren',
            categories: [{ key: 'k', name: 'Minioren Garde', part: 1, solo: false, jurors: 3, submitted: 3, complete: true, rows: [row(2, 'Flames', 90), row(1, 'Mini Drammers', 97)] }],
          },
        ],
      },
      '/api/v1/festival-jury/head/awards': {},
    });
    await renderApp(routes, '/dansgardefestival-jury/hoofdjury');
    expect(await screen.findByLabelText('2e plaats: Flames, CV, 90 punten')).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Prijsuitreiking afronden' }));
    expect(alert).toHaveBeenCalledWith('Prijsuitreiking afgerond?', expect.any(String), expect.any(Array));
    await waitFor(() => expect(requests('/festival-jury/head/awards', 'POST')).toHaveLength(1));
    alert.mockRestore();
  });

  /**
   * Artikel 7 van het reglement: blijft het na uitvoering, moeilijkheidsgraad en choreografie gelijk, dan beslist de
   * voorzitter van de jury. De app zegt dat erbij, want de hoofdjury leest de uitslag hier voor.
   */
  it('hoofdjury: een gelijke stand vraagt een keuze van de voorzitter', async () => {
    const tie = (names: string) => ({
      entryId: names,
      registrationId: 'r',
      place: 1,
      club: 'CV',
      names,
      title: null,
      synchronicity: 0,
      choreography: 16,
      execution: 15,
      difficulty: 14,
      total: 90,
      perJuror: [],
    });
    mockApi({
      ...api,
      '/api/v1/me': me(['festival.head-jury']),
      '/api/v1/festival-jury/head': {
        published: true,
        festivalName: 'Dansgardefestival 2027',
        awardsCompletedAt: null,
        parts: [
          {
            part: 1,
            name: 'Deel 1 · Minioren',
            categories: [{ key: 'k', name: 'Minioren Garde', part: 1, solo: false, jurors: 3, submitted: 3, complete: true, rows: [tie('Flames'), tie('Mini Drammers')] }],
          },
        ],
      },
      '/api/v1/festival-jury/head/awards': {},
    });
    await renderApp(routes, '/dansgardefestival-jury/hoofdjury');

    expect(await screen.findByLabelText('1e plaats (gelijke stand, jij beslist): Flames, CV, 90 punten')).toBeTruthy();
    expect(screen.getByLabelText('1e plaats (gelijke stand, jij beslist): Mini Drammers, CV, 90 punten')).toBeTruthy();
    expect(screen.getByText(/Gelijke stand: ook uitvoering/)).toBeTruthy();
  });
});

/** 2026-11-02: jury-modus — een jurylid van buiten de vereniging ziet alleen het jureren en een uitlogknop. */
describe('Jury-modus', () => {
  const juryRoutes = {
    '(tabs)/_layout': TabLayout,
    '(tabs)/index': HomeScreen,
    'dansgardefestival-jury/index': FestivalJuryScreen,
  };

  it('een jurylid zonder lidmaatschap komt na inloggen direct op het jureerscherm, zonder tabbalk', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me(['festival.judge'], { roles: juryRole }),
      '/api/v1/festival-jury': session(),
    });
    await renderApp(juryRoutes, '/');

    expect(await screen.findByText('Nu aan de beurt')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Uitloggen' })).toBeTruthy();
    // Geen tabbalk en geen weg terug naar de rest van de app.
    expect(screen.queryByText('Programma')).not.toBeNull(); // de kaart Programma hoort er wél te zijn
    expect(screen.queryByRole('button', { name: 'Meer' })).toBeNull();
    expect(screen.queryByText('De Vrolijke Drammers')).toBeNull();
  });

  it('een lid dat ook jurylid is houdt de gewone app', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me(['festival.judge'], { memberId: 'm-1', roles: juryRole }),
      '/api/v1/festival-jury': session(),
    });
    await renderApp(juryRoutes, '/dansgardefestival-jury');

    expect(await screen.findByText('Nu aan de beurt')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Uitloggen' })).toBeNull();
    expect(screen.getByText('Meer')).toBeTruthy();
  });

  it('staat er geen festival klaar, dan een rustige melding met alleen uitloggen', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me(['festival.judge'], { roles: juryRole }),
      '/api/v1/festival-jury': { status: 404 },
    });
    await renderApp(juryRoutes, '/dansgardefestival-jury');

    expect(await screen.findByText('Nog geen festival')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Uitloggen' })).toBeTruthy();
  });
});
