import AsyncStorage from '@react-native-async-storage/async-storage';
import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Alert, StyleSheet } from 'react-native';
import OptochtScreen from '../app/(tabs)/optocht';
import BeoordelenScreen from '../app/jureren/beoordelen';
import JurerenScreen from '../app/jureren';
import UitslagenScreen from '../app/uitslagen';
import { setSessionForTest } from '../auth/session';
import { api } from '../test/api-fixture';
import { mockApi, PHONE_SIZE, renderApp, setWindowSize, TABLET_SIZE } from '../test/render';

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
  // Weging per beoordeelde categorie; bepaalt het totaal dat het jurylid in het overzicht ziet.
  weights: [
    { categoryId: 1, originality: 1, carnivalesque: 1, quality: 2, overall: 1 },
    { categoryId: 3, originality: 1, carnivalesque: 1, quality: 1, overall: 2 },
  ],
});

const requests = (path: string, method: string) =>
  (globalThis.fetch as jest.Mock).mock.calls
    .map(([input, init]) => (typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request)))
    .filter((r) => r.url.includes(path) && r.method === method);

afterEach(() => setWindowSize(PHONE_SIZE));

beforeEach(async () => {
  (globalThis.fetch as jest.Mock).mockClear();
  setSessionForTest('signedIn');
  // Scores staan op de telefoon; zonder wissen neemt de volgende test die van de vorige mee.
  await AsyncStorage.clear();
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
    // Dezelfde wagen staat op een pagina per voorbijtrekken, dus hij komt meer dan één keer voor.
    expect(await screen.findAllByText('De Beunhazen')).not.toHaveLength(0);
    expect(screen.getAllByText('“Importheffingen Trump”')).not.toHaveLength(0);
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
    expect(screen.getAllByText(/Niet aan jou toegewezen/)).not.toHaveLength(0);
    alert.mockRestore();
  });

  it('doorswipen: na de laatste wagen van een voorbijtrekken komt de eerste wagen van het volgende', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/jury/current': session() });
    await renderApp(routes, '/jureren/beoordelen?start=r-7&passage=1');
    // nr. 7 is de laatste van de eigen categorieën, dus hierna begint voorbij 2 weer bij nr. 5.
    expect(await screen.findByText('2 van 2')).toBeTruthy();
    expect(screen.getByText('Voorbij 2 · nr. 5 ›')).toBeTruthy();
  });

  it('tablet: de deelnemerslijst staat ernaast en springt naar een andere wagen', async () => {
    setWindowSize(TABLET_SIZE);
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/jury/current': session() });
    await renderApp(routes, '/jureren/beoordelen?start=r-5&passage=1');
    expect(await screen.findByText('Jouw categorieën')).toBeTruthy();
    expect(screen.getByText('2 deelnemers · 0 beoordeeld')).toBeTruthy();
    // Tikken in de lijst gaat naar die wagen; de kop telt binnen het voorbijtrekken.
    expect(await screen.findByText('1 van 2')).toBeTruthy();
    await fireEvent.press(screen.getByLabelText('7, De Snotapen, nog niet beoordeeld'));
    expect(await screen.findByText('2 van 2')).toBeTruthy();
  });

  it('tablet: de lijst blijft staan als je naar het overzicht gaat', async () => {
    setWindowSize(TABLET_SIZE);
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/jury/current': session() });
    await renderApp(routes, '/jureren/beoordelen?start=r-5&passage=1');
    expect(await screen.findByText('Jouw categorieën')).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Overzicht' }));
    expect(await screen.findByText('Klaar met jureren?')).toBeTruthy();
    // Verdween de lijst hier, dan bleef die ruimte leeg terwijl de pagina's smal blijven.
    expect(screen.getByText('Jouw categorieën')).toBeTruthy();
  });

  it('tablet: de lijst krijgt hoogte, anders zie je alleen de kop', async () => {
    setWindowSize(TABLET_SIZE);
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/jury/current': session() });
    await renderApp(routes, '/jureren/beoordelen?start=r-5&passage=1');
    expect(await screen.findByText('Jouw categorieën')).toBeTruthy();
    // Zonder flex op de kaart is de lijst erbinnen nul hoog: dan staat de kop er wel en de inzendingen niet.
    // Zo gebeurde het op de iPad (5-11-2026), terwijl de rijen in de test gewoon renderden.
    const kaart = screen.getByTestId('jury-column');
    expect(StyleSheet.flatten(kaart.props.style)).toMatchObject({ flex: 1 });
  });

  it('tablet: ook met één categorie staan de wagens onder een kopje', async () => {
    setWindowSize(TABLET_SIZE);
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/jury/current': session() });
    await renderApp(routes, '/jureren/beoordelen?start=r-5&passage=1');
    expect(await screen.findByText('Jouw categorieën')).toBeTruthy();
    // Je jureert hier alleen Getrokken wagens; dat kopje hoort er dan ook te staan.
    expect(screen.getAllByRole('header').map((h) => h.props.children)).toContain('Getrokken wagens volwassenen');
  });

  it('tablet: bij de hele optocht staan de wagens onder een kopje per categorie', async () => {
    setWindowSize(TABLET_SIZE);
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/jury/current': session() });
    const alert = jest.spyOn(Alert, 'alert').mockImplementation((_t, _m, buttons) => buttons?.at(-1)?.onPress?.());
    await renderApp(routes, '/jureren/beoordelen');
    // Met alleen de eigen categorie valt er niets te groeperen, dus dan geen kopjes.
    expect(await screen.findByText('Jouw categorieën')).toBeTruthy();
    expect(screen.queryByText('Loopgroepen groot jeugd')).toBeNull();
    await fireEvent.press(screen.getByRole('switch', { name: 'Hele optocht' }));
    expect(await screen.findByText('1 van 3')).toBeTruthy();
    // De categorienaam staat ook als chip bij de inzending zelf; het kopje in de lijst is een header.
    const kopjes = screen.getAllByRole('header').map((h) => h.props.children);
    expect(kopjes).toContain('Getrokken wagens volwassenen');
    expect(kopjes).toContain('Loopgroepen groot jeugd');
    alert.mockRestore();
  });

  it('op een telefoon staat die lijst er niet; daar blijf je swipen', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/jury/current': session() });
    await renderApp(routes, '/jureren/beoordelen?start=r-5&passage=1');
    expect(await screen.findByText('1 van 2')).toBeTruthy();
    expect(screen.queryByText('Jouw categorieën')).toBeNull();
  });

  it('overgenomen waardes staan er als startpunt en tellen pas na bevestigen mee', async () => {
    const scored = (criterion: string, value: number) => ({ registrationId: 'r-5', pass: 1, criterion, value, scoredAt: '2027-02-07T13:40:00Z' });
    mockApi({
      ...api,
      '/api/v1/me': me,
      '/api/v1/jury/current': {
        ...session(),
        entries: [session().entries[0]!],
        scores: [scored('Originality', 48), scored('Carnivalesque', 40), scored('Quality', 35), scored('Overall', 42)],
      },
      '/api/v1/jury/parades/p-1/scores': { changed: 4 },
    });
    await renderApp(routes, '/jureren/beoordelen?start=r-5&passage=2');

    // Voorbij 2 laat de 48 uit voorbij 1 zien, maar nog niet als ingevuld.
    const sliders = await screen.findAllByLabelText('Originaliteit');
    expect(
      sliders.some((n) => String((n.props as { accessibilityValue?: { text?: string } }).accessibilityValue?.text ?? '').includes('overgenomen uit voorbij 1')),
    ).toBe(true);

    await fireEvent.press(screen.getAllByRole('button', { name: 'Vorige waardes overnemen' })[0]!);
    await waitFor(() => expect(requests('/scores', 'PUT')).toHaveLength(1), { timeout: 4000 });
    const body = await requests('/scores', 'PUT')[0]!.json();
    expect(body.scores).toHaveLength(4);
    expect(body.scores).toEqual(
      expect.arrayContaining([expect.objectContaining({ registrationId: 'r-5', pass: 2, criterion: 'Originality', value: 48 })]),
    );
  });

  it('aantekening per voorbijtrekken: bewaard, verstuurd en terug te lezen bij het indienen', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/jury/current': session(), '/api/v1/jury/parades/p-1/scores': { changed: 1 } });
    await renderApp(routes, '/jureren/beoordelen?start=r-5&passage=1');
    expect(await screen.findByText('1 van 2')).toBeTruthy();
    await fireEvent.changeText(screen.getAllByLabelText('Aantekening voor jezelf')[0]!, 'Muziek te zacht');

    await waitFor(() => expect(requests('/jury/parades/p-1/scores', 'PUT').length).toBeGreaterThan(0), { timeout: 4000 });
    const bodies = await Promise.all(requests('/jury/parades/p-1/scores', 'PUT').map((r) => r.json()));
    expect(bodies.flatMap((b) => b.comments ?? [])).toContainEqual(
      expect.objectContaining({ registrationId: 'r-5', pass: 1, text: 'Muziek te zacht' }),
    );

    // Bij het indienen staan ze bij elkaar, zodat het jurylid ze nog eens nakijkt.
    await fireEvent.press(screen.getByRole('button', { name: 'Overzicht' }));
    expect(await screen.findByText('Jouw aantekeningen')).toBeTruthy();
    expect(screen.getByText('Voorbij 1: Muziek te zacht')).toBeTruthy();
  });

  it('overzicht: gewogen totaal per wagen, en "ga naar inzending" springt naar het eerste gat', async () => {
    const scored = (criterion: string, value: number) => ({ registrationId: 'r-5', pass: 1, criterion, value, scoredAt: '2027-02-07T13:40:00Z' });
    mockApi({
      ...api,
      '/api/v1/me': me,
      // Alleen voorbij 1 van nr. 5 is ingevuld; weging kwaliteit telt dubbel.
      '/api/v1/jury/current': {
        ...session(),
        scores: [scored('Originality', 48), scored('Carnivalesque', 40), scored('Quality', 35), scored('Overall', 42)],
      },
    });
    await renderApp(routes, '/jureren/beoordelen?einde=1');
    expect(await screen.findByText('Klaar met jureren?')).toBeTruthy();

    // 48×1 + 40×1 + 35×2 + 42×1 = 200, van maximaal 100 × (1+1+2+1) = 500.
    expect(screen.getByLabelText(/nr\. 5 De Beunhazen, 200 punten van 500/)).toBeTruthy();
    expect(screen.getByText('200 •')).toBeTruthy();
    // Zonder punten staat er "open".
    expect(screen.getByLabelText(/nr\. 7 De Snotapen, nog geen punten/)).toBeTruthy();

    // Eerste gat is voorbij 1 bij nr. 7, want nr. 5 is daar al af.
    await fireEvent.press(screen.getByRole('button', { name: 'Ga naar inzending' }));
    expect(await screen.findByText('2 van 2')).toBeTruthy();
  });

  it('foto: na versturen verdwijnt "wacht op internet" en staat er dat hij verwerkt wordt', async () => {
    let photos: unknown[] = [];
    mockApi({
      ...api,
      '/api/v1/me': me,
      '/api/v1/jury/current': () => ({ ...session(), photos }),
      '/api/v1/jury/parades/p-1/entries/r-5/photo': () => {
        // Na de upload kent de server de foto, maar hij wordt nog verwerkt (geen miniatuur).
        photos = [{ registrationId: 'r-5', status: 'Pending', thumbnailUrl: null }];
        return { status: 201, body: { id: 'f-1' } };
      },
    });
    (globalThis as { __pick?: unknown }).__pick = { canceled: false, assets: [{ uri: 'file:///tmp/wagen.jpg' }] };
    try {
      await renderApp(routes, '/jureren/beoordelen?start=r-5&passage=1');
      await fireEvent.press((await screen.findAllByRole('button', { name: 'Foto maken' }))[0]!);
      // Zodra de server de foto kent toont de kaart dat hij verwerkt wordt.
      await waitFor(() => expect(screen.getAllByText(/wordt nog verwerkt/)).not.toHaveLength(0), { timeout: 4000 });
      // En dan hoort "wacht op internet" weg te zijn; zonder de fix bleef dat label voor altijd staan.
      expect(screen.queryAllByText('wacht op internet')).toHaveLength(0);
    } finally {
      delete (globalThis as { __pick?: unknown }).__pick;
    }
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
        albumId: 'a-1',
        categories: [
          {
            name: 'Getrokken wagens volwassenen',
            maxPoints: 2500,
            rows: [
              { place: 1, startNumber: 64, groupName: 'De Droatneagels', motto: 'We-j goan deur tot in de 7de hemel.', total: 2204, photoUrl: 'https://example.test/f.jpg' },
              { place: 2, startNumber: 66, groupName: 'De Druktemoakers', motto: null, total: 2101.5, photoUrl: null },
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
