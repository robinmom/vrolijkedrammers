import { fireEvent, screen } from '@testing-library/react-native';
import OptochtScreen from '../app/(tabs)/optocht';
import InschrijvingScreen from '../app/optocht/[id]';
import OptochtInfoScreen from '../app/optocht/info';
import InschrijvenScreen from '../app/optocht/inschrijven';
import { setSessionForTest } from '../auth/session';
import { api } from '../test/api-fixture';
import { mockApi, renderApp } from '../test/render';

const routes = {
  '(tabs)/optocht': OptochtScreen,
  'optocht/inschrijven': InschrijvenScreen,
  'optocht/info': OptochtInfoScreen,
  'optocht/[id]': InschrijvingScreen,
};

const parade = {
  id: 'p-1',
  name: 'Optocht Loil 2027',
  paradeDate: '2027-02-07',
  startTime: '13:30:00',
  startLocation: null,
  routeDescription: null,
  routeLengthKm: null,
  registrationOpensAt: '2026-01-01T09:00:00Z',
  registrationClosesAt: '2099-01-20T23:00:00Z',
  registrationOpen: true,
  subjectRequired: true,
  maxDocumentsPerRegistration: 5,
  maxDocumentSizeMb: 10,
  infoHtml: '<p>Doe mee met je eigen groep!</p>',
};
const categories = [
  {
    id: 3,
    code: 'ADULT_WALK_L',
    name: 'Loopgroep groot',
    ageGroup: 'Adult',
    type: 'WalkingGroupLarge',
    minimumParticipants: 10,
    maximumParticipants: null,
    participantCountBasis: 'AdultsOnly',
    validationMode: 'Block',
    hasVehicle: false,
    active: true,
    sortOrder: 1,
  },
];
const address = {
  street: 'Dorpsstraat',
  houseNumber: '12',
  addition: null,
  postalCode: '6999 AA',
  city: 'Loil',
  country: 'NL',
};
const empty = { street: null, houseNumber: null, addition: null, postalCode: null, city: null, country: null };
const draft = {
  id: 'r-1',
  version: 'v1',
  status: 'Draft',
  registrationNumber: null,
  startNumber: null,
  groupName: 'De Knotwilgen',
  contactName: 'Piet Lid',
  contactPhone: '+31612345678',
  contactPhoneDisplay: '06 12345678',
  contactEmail: 'piet@example.com',
  categoryId: null,
  subject: null,
  subjectDescription: null,
  childrenCount: 0,
  adultCount: 0,
  buildAddress: address,
  juryInspectionSameAsBuildAddress: true,
  juryInspectionAddress: empty,
  estimatedLengthMeters: null,
  additionalInformation: null,
  submittedAt: null,
  withdrawnAt: null,
  editableFields: ['*'],
  canWithdraw: false,
  warnings: [],
  issues: [],
};
const me = (permissions: string[]) => ({
  id: 'u-1',
  email: 'piet@example.com',
  displayName: 'Piet Lid',
  memberId: 'm-1',
  roles: [],
  permissions,
  features: {},
});
const paradeApi = { ...api, '/api/v1/parade/current': parade, '/api/v1/parade/categories': categories };

const bodyOf = async (path: string) => {
  const call = (globalThis.fetch as jest.Mock).mock.calls
    .map(([input, init]) => (typeof input === 'string' ? new Request(input, init as RequestInit) : (input as Request)))
    .filter((r) => r.url.endsWith(path) && r.method !== 'GET')
    .pop()!;
  return call.clone().json();
};

beforeEach(() => (globalThis.fetch as jest.Mock).mockClear());

describe('Optocht inschrijven (fase 11)', () => {
  it('gast: knop zichtbaar, lege velden, code en opgavenummer', async () => {
    setSessionForTest('signedOut', null);
    mockApi({
      ...paradeApi,
      '/api/v1/parade/public-registrations': { status: 201, body: { id: 'g-1' } },
      '/api/v1/parade/public-registrations/g-1/verify-email': { registrationNumber: 4, statusToken: 't' },
    });
    await renderApp(routes, '/optocht');
    await screen.findByText(/Inschrijven kan tot/);
    await fireEvent.press(await screen.findByRole('button', { name: 'Aanmelden optocht' }));

    const groupName = await screen.findByLabelText('Groepsnaam');
    expect(groupName.props.value).toBe('');
    await fireEvent.changeText(groupName, 'De Gasten');
    await fireEvent.press(screen.getByRole('button', { name: 'Volgende' }));
    await fireEvent.changeText(await screen.findByLabelText('Naam'), 'Gerda Gast');
    await fireEvent.changeText(screen.getByLabelText('Telefoon'), '0612345678');
    await fireEvent.changeText(screen.getByLabelText('E-mailadres'), 'gerda@example.com');
    await fireEvent.press(screen.getByRole('button', { name: 'Volgende' }));
    await fireEvent.press(await screen.findByRole('radio', { name: /Loopgroep groot/ }));
    for (let i = 0; i < 12; i++) await fireEvent.press(screen.getByRole('button', { name: 'Volwassenen plus 1' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Volgende' }));
    await fireEvent.changeText(await screen.findByLabelText('Onderwerp'), 'Wilde westen');
    await fireEvent.press(screen.getByRole('button', { name: 'Volgende' }));
    expect(screen.queryByText('Zelfde locatie')).toBeNull();
    await fireEvent.changeText(await screen.findByLabelText('Postcode'), '6999 AB');
    await fireEvent.changeText(screen.getByLabelText('Huisnummer'), '3');
    await fireEvent.changeText(screen.getByLabelText('Straat'), 'Kerkstraat');
    await fireEvent.changeText(screen.getByLabelText('Plaats'), 'Loil');
    await fireEvent.press(screen.getByRole('button', { name: 'Volgende' }));
    await fireEvent.press(await screen.findByRole('button', { name: 'Volgende' }));

    expect(await screen.findByText('STAP 7 VAN 7 · OPTOCHT')).toBeTruthy();
    await fireEvent.press(screen.getByRole('checkbox', { name: /optochtreglement/ }));
    await fireEvent.press(screen.getByRole('button', { name: 'Inschrijving indienen' }));
    expect(await screen.findByText('Bevestig je e-mailadres')).toBeTruthy();
    expect(await bodyOf('/api/v1/parade/public-registrations')).toMatchObject({
      rulesAccepted: true,
      registration: {
        groupName: 'De Gasten',
        categoryId: 3,
        adultCount: 12,
        buildAddress: { street: 'Kerkstraat', city: 'Loil' },
      },
    });

    await fireEvent.changeText(screen.getByLabelText('Code'), '123456');
    await fireEvent.press(screen.getByRole('button', { name: 'Bevestigen' }));
    expect(await screen.findByText('Ingeschreven!')).toBeTruthy();
    expect(screen.getByText('4')).toBeTruthy();
  });

  it('lid zonder recht Groepsverantwoordelijke ziet de informatiepagina uit het portal', async () => {
    setSessionForTest('signedIn');
    mockApi({ ...paradeApi, '/api/v1/me': me(['member.read.own']) });
    await renderApp(routes, '/optocht');
    await fireEvent.press(await screen.findByRole('button', { name: 'Meedoen aan de optocht' }));
    expect(await screen.findByText('Doe mee met je eigen groep!')).toBeTruthy();
    expect(screen.getByText('Een groep inschrijven')).toBeTruthy();
  });

  it('groepsverantwoordelijke: vooringevuld concept, zelfde bouwlocatie en mijn inschrijvingen', async () => {
    setSessionForTest('signedIn');
    const saved = { ...draft, version: 'v2' };
    mockApi({
      ...paradeApi,
      '/api/v1/me': me(['parade.register']),
      '/api/v1/parade/registrations': (method: string) =>
        method === 'POST'
          ? { status: 201, body: draft }
          : [
              {
                id: 'r-9',
                groupName: 'De Knotjes',
                status: 'Submitted',
                registrationNumber: 12,
                startNumber: null,
                submittedAt: null,
                createdAt: '2026-12-01T10:00:00Z',
              },
            ],
      '/api/v1/parade/registrations/r-1': saved,
      '/api/v1/parade/build-locations': [{ id: 'l-1', address, lastUsedAt: '2026-02-01T10:00:00Z' }],
    });
    await renderApp(routes, '/optocht');
    expect(await screen.findByText('De Knotjes')).toBeTruthy();
    expect(screen.getByText('Ingediend – wacht op beoordeling')).toBeTruthy();

    await fireEvent.press(screen.getByRole('button', { name: 'Aanmelden optocht' }));
    expect((await screen.findByLabelText('Groepsnaam')).props.value).toBe('De Knotwilgen');
    await fireEvent.press(screen.getByRole('button', { name: 'Volgende' }));
    expect(await screen.findByText('Concept opgeslagen')).toBeTruthy();
    expect(await bodyOf('/api/v1/parade/registrations/r-1')).toMatchObject({
      version: 'v1',
      groupName: 'De Knotwilgen',
    });
    expect(screen.getByLabelText('Telefoon').props.value).toBe('06 12345678');
    await fireEvent.press(screen.getByRole('button', { name: 'Volgende' }));
    await fireEvent.press(await screen.findByRole('radio', { name: /Loopgroep groot/ }));
    await fireEvent.press(screen.getByRole('button', { name: 'Volwassenen plus 1' }));
    expect(screen.getByText('Deze categorie is voor 10 of meer volwassenen (nu 1).')).toBeTruthy();
    for (let i = 0; i < 9; i++) await fireEvent.press(screen.getByRole('button', { name: 'Volwassenen plus 1' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Volgende' }));
    await fireEvent.changeText(await screen.findByLabelText('Onderwerp'), 'Wilde westen');
    await fireEvent.press(screen.getByRole('button', { name: 'Volgende' }));

    const same = await screen.findByRole('radio', { name: 'Zelfde locatie: Dorpsstraat 12, 6999 AA Loil' });
    expect(same.props.accessibilityState.checked).toBe(true);
    expect(screen.getByRole('button', { name: 'Verwijder Dorpsstraat 12, 6999 AA Loil' })).toBeTruthy();
    await fireEvent.press(screen.getByRole('radio', { name: '+ Nieuwe locatie toevoegen' }));
    expect((await screen.findByLabelText('Straat')).props.value).toBe('');
  });
});
