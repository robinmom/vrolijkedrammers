import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react-native';
import type { ComponentType } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import HomeScreen from '../app/(tabs)/index';
import MeerScreen from '../app/(tabs)/meer';
import NieuwsScreen from '../app/(tabs)/nieuws';
import OptochtScreen from '../app/(tabs)/optocht';
import ProgrammaScreen from '../app/(tabs)/programma';
import ActiviteitScreen from '../app/activiteit/[id]';
import FotosScreen from '../app/fotos/index';
import { ThemeProvider, type ThemeMode } from '../theme/ThemeProvider';
import { api } from '../test/api-fixture';
import { eventDetail } from '../test/fixtures';
import { createTestQueryClient, mockApi } from '../test/render';

// Schermen los renderen (zonder navigator): de snapshot bevat dan alleen het scherm zelf.
jest.mock('expo-router', () => ({
  ...jest.requireActual('expo-router'),
  useFocusEffect: jest.fn(),
  useLocalSearchParams: () => ({ id: '44444444-4444-4444-8444-444444444444' }),
  router: { push: jest.fn(), back: jest.fn(), replace: jest.fn(), canGoBack: () => true },
}));

/** De 7 Figma-schermen met een tekst die pas verschijnt als alle data geladen is. */
const screens: [string, ComponentType, string][] = [
  ['01 Home', HomeScreen, 'Nog tot carnaval 2027'],
  ['02 Programma', ProgrammaScreen, 'Seizoen 2026–2027'],
  ['03 Nieuws', NieuwsScreen, 'Eerder nieuws'],
  ['04 Optocht', OptochtScreen, 'ZONDAG 7 FEBRUARI 2027'],
  ['05 Meer', MeerScreen, 'Word ook een Drammer!'],
  ['06 Activiteit detail', ActiviteitScreen, eventDetail.title],
  ["07 Foto's", FotosScreen, 'Recent toegevoegd'],
];

// ScrollView krijgt de RefreshControl als React-element mee; dat element verwijst naar de fiber-boom en is niet
// te serialiseren. In de snapshot volstaat een markering.
expect.addSnapshotSerializer({
  test: (value) => typeof value?.props?.refreshControl === 'object' && value.props.refreshControl !== null,
  serialize: (value, config, indentation, depth, refs, printer) =>
    printer({ ...value, props: { ...value.props, refreshControl: '[RefreshControl]' } }, config, indentation, depth, refs),
});

beforeEach(() => {
  // Vaste klok (een halve seconde na het hele uur), zodat de countdown in de snapshot niet verspringt.
  jest.useFakeTimers({ now: new Date('2026-09-25T06:00:00.500Z') });
  mockApi(api);
});

afterEach(() => jest.useRealTimers());

describe.each<ThemeMode>(['light', 'dark'])('snapshot %s', (mode) => {
  it.each(screens)('%s', async (_, Screen, loadedText) => {
    const view = await render(
      <SafeAreaProvider initialMetrics={{ frame: { x: 0, y: 0, width: 393, height: 852 }, insets: { top: 54, left: 0, right: 0, bottom: 34 } }}>
        <ThemeProvider mode={mode}>
          <QueryClientProvider client={createTestQueryClient()}>
            <Screen />
          </QueryClientProvider>
        </ThemeProvider>
      </SafeAreaProvider>,
    );
    expect(await screen.findByText(loadedText)).toBeTruthy();
    // Sommige schermen laden in twee stappen (Foto's: eerst albums, dan foto's).
    await waitFor(() => expect(screen.queryByLabelText('Laden')).toBeNull());
    expect(view.toJSON()).toMatchSnapshot();
  });
});
