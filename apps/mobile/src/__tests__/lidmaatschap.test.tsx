import { fireEvent, screen } from '@testing-library/react-native';
import MijnGegevensScreen from '../app/account/index';
import CombinatieScreen from '../app/account/combinatie';
import GegevensWijzigenScreen from '../app/account/wijzigen';
import { setSessionForTest } from '../auth/session';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

/** Fase 26: gegevens wijzigen en een combinatie verbreken vanuit de app (altijd met goedkeuring). */

const routes = {
  'account/index': MijnGegevensScreen,
  'account/wijzigen': GegevensWijzigenScreen,
  'account/combinatie': CombinatieScreen,
};

const me = {
  id: 'u-1',
  email: 'marie@example.com',
  displayName: 'Marie de Vries',
  memberId: 'm-2',
  roles: [{ code: 'lid', name: 'Carnavalist' }],
  permissions: ['member.read.own'],
  features: {},
};
const member = {
  memberNumber: '0201',
  fullName: 'Marie de Vries',
  firstName: 'Marie',
  addressLine: 'Kerkstraat 2',
  postalCode: '6999 AB',
  city: 'Loil',
  email: 'marie@example.com',
  phone: null,
  birthDate: '1966-05-01',
  joinYear: 1993,
  yearsMember: 34,
  isJubilee: false,
  status: 'Active',
  membershipValidTo: null,
  groups: [],
};
const combination = {
  role: 'Partner',
  otherMemberId: 'm-1',
  otherName: 'Jan de Vries',
  breakRequestId: 'b-1',
  breakStatus: 'AwaitingAgreement',
  iAgreed: false,
  otherAgreed: true,
  ibanRequiredFromMe: true,
};

const requests = () =>
  (globalThis.fetch as jest.Mock).mock.calls.map(([input, init]) =>
    typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request),
  );

beforeEach(() => {
  setSessionForTest('signedIn');
  (globalThis.fetch as jest.Mock).mockClear();
});

function mock(extra: Record<string, unknown> = {}) {
  mockApi({
    ...api,
    '/api/v1/me': me,
    '/api/v1/me/member': { status: 200, body: member },
    '/api/v1/me/membership-requests': { status: 200, body: { latestChange: null, combination } },
    '/api/v1/me/change-requests': { status: 201, body: { id: 'c-1' } },
    '/api/v1/me/combination-break/agree': { status: 204 },
    '/api/v1/me/combination-break/cancel': { status: 204 },
    ...extra,
  });
}

describe('Mijn gegevens: wijzigen en combinatie', () => {
  it('toont de combinatie en het verzoek om akkoord', async () => {
    mock();
    await renderApp(routes, '/account');
    expect(await screen.findByText(/Je bent samen met Jan de Vries lid/)).toBeTruthy();
    expect(screen.getByText('Jan de Vries betaalt de contributie.', { exact: false })).toBeTruthy();
    expect(screen.getByText('Jan de Vries wil de combinatie verbreken. Geef je akkoord?')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Gegevens wijzigen' })).toBeTruthy();
  });

  it('wijziging met nieuwe IBAN pas versturen met machtiging', async () => {
    mock();
    await renderApp(routes, '/account/wijzigen');
    const address = await screen.findByLabelText('Straat en huisnummer');
    expect(address.props.value).toBe('Kerkstraat 2');
    await fireEvent.changeText(address, 'Kerkstraat 5');
    await fireEvent.changeText(screen.getByLabelText('IBAN'), 'NL91 ABNA 0417 1643 00');
    const send = screen.getByRole('button', { name: 'Wijziging versturen' });
    expect(send.props.accessibilityState.disabled).toBe(true);
    await fireEvent.changeText(screen.getByLabelText('Naam rekeninghouder'), 'M. de Vries');
    await fireEvent.press(screen.getByRole('checkbox'));
    await fireEvent.press(send);

    expect(await screen.findByText(/De ledenadministratie beoordeelt je wijziging/)).toBeTruthy();
    const post = requests().find((r) => r.url.endsWith('/api/v1/me/change-requests') && r.method === 'POST')!;
    expect(await post.clone().json()).toMatchObject({
      addressLine: 'Kerkstraat 5',
      postalCode: '6999 AB',
      iban: 'NL91 ABNA 0417 1643 00',
      accountHolder: 'M. de Vries',
      mandateConsent: true,
    });
  });

  it('akkoord op verbreken: het tweede lid geeft zijn IBAN', async () => {
    mock();
    await renderApp(routes, '/account/combinatie');
    expect(await screen.findByText(/Jan de Vries wil jullie combinatie verbreken/)).toBeTruthy();
    const agree = screen.getByRole('button', { name: 'Akkoord' });
    expect(agree.props.accessibilityState.disabled).toBe(true);
    await fireEvent.changeText(screen.getByLabelText('IBAN'), 'NL91ABNA0417164300');
    await fireEvent.changeText(screen.getByLabelText('Naam rekeninghouder'), 'M. de Vries');
    await fireEvent.press(screen.getByRole('checkbox'));
    await fireEvent.press(agree);

    const post = await (async () => {
      for (let i = 0; i < 20; i++) {
        const found = requests().find((r) => r.url.endsWith('/combination-break/agree'));
        if (found) return found;
        await new Promise((resolve) => setTimeout(resolve, 10));
      }
      throw new Error('geen akkoord verstuurd');
    })();
    expect(await post.clone().json()).toEqual({
      iban: 'NL91ABNA0417164300',
      accountHolder: 'M. de Vries',
      mandateConsent: true,
    });
  });

  it('niet akkoord trekt het verzoek in', async () => {
    mock();
    await renderApp(routes, '/account/combinatie');
    await fireEvent.press(await screen.findByRole('button', { name: 'Niet akkoord' }));
    await screen.findByRole('button', { name: 'Niet akkoord' });
    expect(requests().some((r) => r.url.endsWith('/combination-break/cancel') && r.method === 'POST')).toBe(true);
  });
});
