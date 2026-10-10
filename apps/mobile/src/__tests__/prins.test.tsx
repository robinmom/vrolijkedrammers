import { fireEvent, screen } from '@testing-library/react-native';
import HomeScreen from '../app/(tabs)/index';
import PrinsInfoScreen from '../app/prins-info';
import { setSessionForTest } from '../auth/session';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

/** Prins(es) en adjudanten (2026-10-10): eigen begroeting en een knop Info op de eerste pagina. */

const routes = { '(tabs)/index': HomeScreen, 'prins-info': PrinsInfoScreen };

const me = {
  id: 'u-1',
  email: 'bram@example.com',
  displayName: 'Bram Drammer',
  memberId: 'm-1',
  roles: [],
  permissions: ['member.read.own'],
  features: {},
};

beforeEach(() => setSessionForTest('signedIn'));

describe('Prins en adjudanten', () => {
  it('begroet de adjudant en toont de info achter de knop', async () => {
    mockApi({
      ...api,
      '/api/v1/me': me,
      '/api/v1/me/royal': {
        roleCode: 'adjudant',
        greeting: 'Welkom adjudant Bram van prinses Anna',
        infoHtml: '<p>Haal de prinses om <strong>19:00</strong> op.</p>',
      },
    });
    await renderApp(routes, '/');
    expect(await screen.findByText('Welkom adjudant Bram van prinses Anna!')).toBeTruthy();
    expect(screen.getByText('Informatie voor de adjudanten')).toBeTruthy();

    await fireEvent.press(screen.getByRole('button', { name: 'Info' }));
    expect(await screen.findByText(/Haal de prinses om/)).toBeTruthy();
  });

  it('een gewoon lid ziet de gewone groet en geen knop', async () => {
    mockApi({ ...api, '/api/v1/me': me, '/api/v1/me/royal': { status: 204 } });
    await renderApp(routes, '/');
    expect(await screen.findByText(/, Bram!|, Drammer!/)).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Info' })).toBeNull();
  });
});
