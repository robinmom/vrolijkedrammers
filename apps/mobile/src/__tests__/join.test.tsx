import { fireEvent, screen } from '@testing-library/react-native';
import TabLayout from '../app/(tabs)/_layout';
import MeerScreen from '../app/(tabs)/meer';
import AanmeldenScreen from '../app/meer/aanmelden';
import LidWordenScreen from '../app/meer/lid-worden';
import { setSessionForTest } from '../auth/session';
import { ageOn, parseDutchDate } from '../features/membership';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

const routes = {
  '(tabs)/_layout': TabLayout,
  '(tabs)/meer': MeerScreen,
  'meer/lid-worden': LidWordenScreen,
  'meer/aanmelden': AanmeldenScreen,
};

const requests = () =>
  (globalThis.fetch as jest.Mock).mock.calls.map(([input, init]) =>
    typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request),
  );

beforeEach(() => {
  setSessionForTest('signedOut', null);
  (globalThis.fetch as jest.Mock).mockClear();
});

async function fillAdult(birthDate = '12-03-1990') {
  await fireEvent.changeText(await screen.findByLabelText('Voornaam'), 'Piet');
  await fireEvent.changeText(screen.getByLabelText('Achternaam'), 'Berg');
  await fireEvent.changeText(screen.getByLabelText('Geboortedatum'), birthDate);
  await fireEvent.changeText(screen.getByLabelText('Straat en huisnummer'), 'Dorpsstraat 1');
  await fireEvent.changeText(screen.getByLabelText('Postcode'), '6999 AA');
  await fireEvent.changeText(screen.getByLabelText('Woonplaats'), 'Loil');
}

async function fillRest(emailLabel = 'E-mailadres') {
  await fireEvent.changeText(screen.getByLabelText(emailLabel), 'piet@example.com');
  await fireEvent.changeText(screen.getByLabelText('IBAN'), 'NL91 ABNA 0417 1643 00');
  await fireEvent.changeText(screen.getByLabelText('Naam rekeninghouder'), 'P. Berg');
  await fireEvent.press(screen.getByRole('checkbox', { name: /doorlopende incasso/ }));
  await fireEvent.press(screen.getByRole('checkbox', { name: /privacyverklaring/ }));
}

describe('Lid worden', () => {
  it('van de informatiepagina naar het formulier', async () => {
    mockApi(api);
    await renderApp(routes, '/meer/lid-worden');
    await fireEvent.press(await screen.findByRole('button', { name: 'Aanmelden' }));
    expect(await screen.findByText('Het nieuwe lid')).toBeTruthy();
  });

  it('volwassene: versturen, code bevestigen en klaar', async () => {
    mockApi({
      ...api,
      '/api/v1/membership-applications': { status: 201, body: { id: 'a-1' } },
      '/api/v1/membership-applications/a-1/verify-email': { status: 204 },
    });
    await renderApp(routes, '/meer/aanmelden');
    await fillAdult();
    expect(screen.queryByText('Ouder of verzorger')).toBeNull();
    const send = screen.getByRole('button', { name: 'Aanmelding versturen' });
    expect(send.props.accessibilityState.disabled).toBe(true);
    await fillRest();
    await fireEvent.press(send);

    expect(await screen.findByText('Bevestig je e-mailadres')).toBeTruthy();
    const post = requests().find((r) => r.url.endsWith('/api/v1/membership-applications'))!;
    expect(await post.clone().json()).toMatchObject({
      birthDate: '1990-03-12',
      email: 'piet@example.com',
      mandateConsent: true,
      privacyConsent: true,
      photoConsent: false,
      guardianName: null,
      source: 'App',
    });

    await fireEvent.changeText(screen.getByLabelText('Code'), '123456');
    await fireEvent.press(screen.getByRole('button', { name: 'Bevestigen' }));
    expect(await screen.findByText('Bedankt voor je aanmelding!')).toBeTruthy();
  });

  it('onder de 16: sectie ouder/verzorger is verplicht en het e-mailadres is dat van de ouder', async () => {
    mockApi({ ...api, '/api/v1/membership-applications': { status: 201, body: { id: 'a-2' } } });
    const tenYearsAgo = new Date();
    tenYearsAgo.setFullYear(tenYearsAgo.getFullYear() - 10);
    await renderApp(routes, '/meer/aanmelden');
    await fillAdult(`01-01-${tenYearsAgo.getFullYear()}`);

    expect(await screen.findByText('Ouder of verzorger')).toBeTruthy();
    await fillRest('E-mailadres ouder/verzorger');
    const send = screen.getByRole('button', { name: 'Aanmelding versturen' });
    expect(send.props.accessibilityState.disabled).toBe(true);
    await fireEvent.changeText(screen.getByLabelText('Naam ouder/verzorger'), 'Anja Berg');
    await fireEvent.changeText(screen.getByLabelText('Telefoon ouder/verzorger'), '0612345678');
    await fireEvent.press(send);

    expect(await screen.findByText('Bevestig je e-mailadres')).toBeTruthy();
    const post = requests().find((r) => r.url.endsWith('/api/v1/membership-applications'))!;
    expect(await post.clone().json()).toMatchObject({ guardianName: 'Anja Berg', guardianPhone: '0612345678' });
  });

  it('toont de uitleg van de API bij een fout (bijv. ongeldig IBAN)', async () => {
    mockApi({
      ...api,
      '/api/v1/membership-applications': { status: 422, body: { detail: 'Het IBAN-nummer is ongeldig.' } },
    });
    await renderApp(routes, '/meer/aanmelden');
    await fillAdult();
    await fillRest();
    await fireEvent.press(screen.getByRole('button', { name: 'Aanmelding versturen' }));
    expect(await screen.findByText('Het IBAN-nummer is ongeldig.')).toBeTruthy();
  });
});

describe('datums', () => {
  it('leest Nederlandse datums en berekent de leeftijd', () => {
    expect(parseDutchDate('1-2-2015')).toBe('2015-02-01');
    expect(parseDutchDate('31-02-2015')).toBeNull();
    expect(parseDutchDate('2015-02-01')).toBeNull();
    expect(ageOn('2010-06-15', new Date(2026, 5, 14))).toBe(15);
    expect(ageOn('2010-06-15', new Date(2026, 5, 15))).toBe(16);
  });
});
