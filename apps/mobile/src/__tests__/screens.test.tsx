import { useNetInfo } from '@react-native-community/netinfo';
import { onlineManager, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { createEventInCalendarAsync } from 'expo-calendar/legacy';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import TabLayout from '../app/(tabs)/_layout';
import HomeScreen from '../app/(tabs)/index';
import MeerScreen from '../app/(tabs)/meer';
import NieuwsScreen from '../app/(tabs)/nieuws';
import OptochtScreen from '../app/(tabs)/optocht';
import ProgrammaScreen from '../app/(tabs)/programma';
import ActiviteitScreen from '../app/activiteit/[id]';
import FotoViewer from '../app/fotos/[id]/[photoId]';
import AlbumScreen from '../app/fotos/[id]/index';
import FotosScreen from '../app/fotos/index';
import WeergaveScreen from '../app/meer/weergave';
import NieuwsBerichtScreen from '../app/nieuws/[id]';
import UitslagenScreen from '../app/uitslagen';
import { AppGate } from '../shell/AppGate';
import { ThemeProvider } from '../theme/ThemeProvider';
import { api, paged } from '../test/api-fixture';
import * as data from '../test/fixtures';
import { createTestQueryClient, mockApi, renderApp } from '../test/render';
import { AppText, OfflineBanner } from '../ui';

const routes = {
  '(tabs)/_layout': TabLayout,
  '(tabs)/index': HomeScreen,
  '(tabs)/programma': ProgrammaScreen,
  '(tabs)/optocht': OptochtScreen,
  '(tabs)/nieuws': NieuwsScreen,
  '(tabs)/meer': MeerScreen,
  'activiteit/[id]': ActiviteitScreen,
  'nieuws/[id]': NieuwsBerichtScreen,
  'fotos/index': FotosScreen,
  'fotos/[id]/index': AlbumScreen,
  'fotos/[id]/[photoId]': FotoViewer,
  'meer/weergave': WeergaveScreen,
  uitslagen: UitslagenScreen,
};

beforeEach(() => {
  // Vrijdag 25 september 2026, 08:00 in Loil; de klok loopt door zodat queries en timers gewoon werken.
  jest.useFakeTimers({ now: new Date('2026-09-25T06:00:00Z'), advanceTimers: true });
});

afterEach(() => {
  jest.useRealTimers();
  onlineManager.setOnline(true);
});

describe('01 Home', () => {
  it('toont groet, countdown, eerstvolgende activiteit en laatste nieuws', async () => {
    mockApi(api);
    await renderApp(routes, '/');
    expect(await screen.findByText('Elfde van de Elfde')).toBeTruthy();
    expect(screen.getByText('Goedemorgen, Drammer!')).toBeTruthy();
    expect(screen.getByText('11:11 uur · Dorpsplein Loil')).toBeTruthy();
    expect(await screen.findByText('De optocht-inschrijving is geopend!')).toBeTruthy();
    // Voorleestekst van de countdown: tot 6 februari 2027 00:00 in Loil.
    expect(screen.getByLabelText('Nog 133 dagen, 17 uur, 0 minuten tot carnaval 2027')).toBeTruthy();
  });

  it('de snelkoppeling Foto’s opent de albums', async () => {
    mockApi(api);
    await renderApp(routes, '/');
    await fireEvent.press(await screen.findByLabelText("Foto's"));
    expect(await screen.findByText('Pronkzitting 2026')).toBeTruthy();
  });
});

describe('02 Programma', () => {
  it('groepeert per maand, toont badges en filtert op categorie', async () => {
    mockApi(api);
    await renderApp(routes, '/programma');
    expect(await screen.findByText('NOVEMBER 2026')).toBeTruthy();
    expect(screen.getByText('JANUARI 2027')).toBeTruthy();
    expect(screen.getByText('FEBRUARI 2027')).toBeTruthy();
    expect(screen.getByText('Hoogtepunt')).toBeTruthy();
    expect(await screen.findByText('Seizoen 2026–2027')).toBeTruthy();
    // Vereniging heeft geen activiteiten en krijgt dus geen chip.
    expect(screen.queryByRole('button', { name: 'Vereniging' })).toBeNull();

    await fireEvent.press(screen.getByRole('button', { name: 'Jeugd' }));
    expect(screen.queryByText('Elfde van de Elfde')).toBeNull();
    expect(screen.getByText('Kindermiddag')).toBeTruthy();
  });

  it('zoekt op titel', async () => {
    mockApi(api);
    await renderApp(routes, '/programma');
    await screen.findByText('Elfde van de Elfde');
    await fireEvent.press(screen.getByLabelText('Zoeken'));
    await fireEvent.changeText(screen.getByLabelText('Zoek een activiteit'), 'optocht');
    expect(screen.queryByText('Elfde van de Elfde')).toBeNull();
    expect(screen.getByText('Optocht Loil')).toBeTruthy();
  });
});

describe('06 Activiteit detail', () => {
  it('toont de details en zet de activiteit in de agenda', async () => {
    mockApi(api);
    await renderApp(routes, `/activiteit/${data.eventDetail.id}`);
    expect(await screen.findByText('Pronkzitting 2027')).toBeTruthy();
    expect(screen.getByText('Zaterdag 16 januari 2027')).toBeTruthy();
    expect(screen.getByText('20:00 – 00:30 uur')).toBeTruthy();
    expect(screen.getByText('Bijna uitverkocht')).toBeTruthy();
    expect(screen.getByText('büttenreden')).toBeTruthy();
    // De ticket-CTA volgt pas in fase 19.
    expect(screen.queryByText('Tickets bestellen')).toBeNull();

    await fireEvent.press(screen.getByLabelText('Pronkzitting 2027 toevoegen aan agenda'));
    await waitFor(() =>
      expect(createEventInCalendarAsync).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'Pronkzitting 2027', location: 'De Drammersbühne, Dorpsstraat 12, Loil' }),
      ),
    );
  });

  it('een ongeldige deeplink raakt de API niet', async () => {
    const calls = mockApi(api);
    await renderApp(routes, '/activiteit/..%2F..%2Fadmin');
    expect(await screen.findByText('Activiteit niet gevonden')).toBeTruthy();
    expect(calls.filter((c) => c.startsWith('/api/v1/events'))).toEqual([]);
  });

  it('404 van de API = niet gevonden (bestaan niet lekken)', async () => {
    mockApi({ ...api, [`/api/v1/events/${data.eventDetail.id}`]: { status: 404 } });
    await renderApp(routes, `/activiteit/${data.eventDetail.id}`);
    expect(await screen.findByText('Activiteit niet gevonden')).toBeTruthy();
  });
});

describe('03 Nieuws', () => {
  it('uitgelicht bericht, eerder nieuws en detail', async () => {
    mockApi(api);
    await renderApp(routes, '/nieuws');
    expect(await screen.findByText('De optocht-inschrijving is geopend!')).toBeTruthy();
    expect(screen.getByText('Eerder nieuws')).toBeTruthy();
    expect(screen.getByText('Uitslag Dansgarde Festival 2026')).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Lees meer' }));
    expect(await screen.findByText('optocht')).toBeTruthy();
  });

  it('nieuw gepubliceerd nieuws verschijnt bij terugkeren naar de tab (zonder pull-to-refresh)', async () => {
    mockApi(api);
    await renderApp(routes, '/nieuws');
    expect(await screen.findByText('De optocht-inschrijving is geopend!')).toBeTruthy();

    // Intussen publiceert een redacteur een nieuw bericht in het portal.
    const nieuw = { ...data.news[0]!, id: '99999999-9999-4999-8999-999999999999', title: 'Prins Ferry I bekendgemaakt', publishedAt: '2026-09-25T06:30:00Z' };
    mockApi({ ...api, '/api/v1/news': paged([nieuw, ...data.news]) });

    await fireEvent.press(screen.getAllByText('Home')[0]!);
    jest.advanceTimersByTime(31_000);
    await fireEvent.press(screen.getAllByText('Nieuws').at(-1)!);
    expect(await screen.findByText('Prins Ferry I bekendgemaakt')).toBeTruthy();
  });

  it('pull-to-refresh haalt direct nieuwe content op', async () => {
    mockApi(api);
    await renderApp(routes, '/nieuws');
    await screen.findByText('De optocht-inschrijving is geopend!');
    const nieuw = { ...data.news[0]!, id: '99999999-9999-4999-8999-999999999998', title: 'Net gepubliceerd' };
    mockApi({ ...api, '/api/v1/news': paged([nieuw, ...data.news]) });

    // De RefreshControl-mock geeft zijn props niet door; de ScrollView houdt het element wel vast als prop.
    type Node = NonNullable<typeof screen.root>;
    const findScroll = (node: Node): Node | undefined =>
      node.props.refreshControl ? node : node.children.filter((c): c is Node => typeof c !== 'string').map(findScroll).find(Boolean);
    const onRefresh = findScroll(screen.root!)!.props.refreshControl.props.onRefresh as () => Promise<void>;
    await act(async () => {
      await onRefresh();
    });
    expect(await screen.findByText('Net gepubliceerd')).toBeTruthy();
  });

  it('Uitslagen toont alleen de categorie Uitslagen', async () => {
    mockApi(api);
    await renderApp(routes, '/uitslagen');
    expect(await screen.findByText('Uitslag Dansgarde Festival 2026')).toBeTruthy();
    expect(screen.queryByText('De optocht-inschrijving is geopend!')).toBeNull();
  });
});

describe("07 Foto's", () => {
  it('albums, recente foto’s en de viewer', async () => {
    mockApi(api);
    await renderApp(routes, '/fotos');
    expect(await screen.findByLabelText("Album Pronkzitting 2026, 2 foto's")).toBeTruthy();
    await fireEvent.press(await screen.findByLabelText('De Prins'));
    expect(await screen.findByText('1 / 2')).toBeTruthy();
    expect(screen.getByText('Foto: Fotograaf')).toBeTruthy();
  });
});

describe('04 Optocht en 05 Meer', () => {
  it('optochtdatum volgt uit het carnavalsjaar', async () => {
    mockApi(api);
    await renderApp(routes, '/optocht');
    expect(await screen.findByText('ZONDAG 7 FEBRUARI 2027')).toBeTruthy();
    expect(screen.getByText('Tijdlijn')).toBeTruthy();
  });

  it('Meer toont de zes tegels en past de weergave aan', async () => {
    mockApi(api);
    await renderApp(routes, '/meer');
    // "Meldingen" staat er twee keer: als icoon in de titelbalk en als tegel.
    for (const label of ['Vereniging', "Foto's", 'Uitslagen', 'Meldingen', 'Locatie', 'Contact']) {
      expect(screen.getAllByRole('button', { name: label }).length).toBeGreaterThan(0);
    }
    expect(screen.getAllByRole('button', { name: 'Meldingen' })).toHaveLength(2);
    await fireEvent.press(screen.getByRole('button', { name: 'Weergave: Automatisch' }));
    expect(await screen.findByText('Licht')).toBeTruthy();
  });

  it('Weergave: kiezen voor donker', async () => {
    mockApi(api);
    await renderApp(routes, '/meer/weergave');
    await fireEvent.press(screen.getByRole('radio', { name: 'Donker' }));
    expect(screen.getByRole('radio', { name: 'Donker' }).props.accessibilityState).toEqual({ checked: true });
    expect(screen.getByRole('radio', { name: 'Automatisch' }).props.accessibilityState).toEqual({ checked: false });
  });
});

describe('app-config', () => {
  async function renderGate() {
    return await render(
      <ThemeProvider mode="light">
        <QueryClientProvider client={createTestQueryClient()}>
          <AppGate>
            <AppText>App</AppText>
          </AppGate>
        </QueryClientProvider>
      </ThemeProvider>,
    );
  }

  it('een hogere minimale versie blokkeert de app', async () => {
    mockApi({ '/api/v1/app-config': { ...data.appConfig, minAppVersion: { ios: '9.0.0', android: '9.0.0' } } });
    await renderGate();
    expect(await screen.findByText('Update nodig')).toBeTruthy();
    expect(screen.queryByText('App')).toBeNull();
  });

  it('onderhoud toont de melding van het bestuur', async () => {
    mockApi({ '/api/v1/app-config': { ...data.appConfig, maintenance: { enabled: true, message: 'Tot vanavond!' } } });
    await renderGate();
    expect(await screen.findByText('Tot vanavond!')).toBeTruthy();
  });

  it('zonder antwoord blijft de app bruikbaar', async () => {
    mockApi({});
    await renderGate();
    expect(await screen.findByText('App')).toBeTruthy();
  });
});

describe('offline', () => {
  it('toont de banner zonder netwerk', async () => {
    jest.mocked(useNetInfo).mockReturnValueOnce({ isConnected: false } as ReturnType<typeof useNetInfo>);
    await render(
      <SafeAreaProvider initialMetrics={{ frame: { x: 0, y: 0, width: 393, height: 852 }, insets: { top: 54, left: 0, right: 0, bottom: 34 } }}>
        <OfflineBanner />
      </SafeAreaProvider>,
    );
    expect(screen.getByText('Je bent offline. Je ziet de laatst geladen gegevens.')).toBeTruthy();
  });

  it('zonder netwerk en zonder cache een nette melding in plaats van een fout', async () => {
    onlineManager.setOnline(false);
    const calls = mockApi(api);
    await renderApp(routes, '/nieuws');
    expect(await screen.findByText('Geen verbinding')).toBeTruthy();
    expect(calls).toEqual([]);
  });
});
