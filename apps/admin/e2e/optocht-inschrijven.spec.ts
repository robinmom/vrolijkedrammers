import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { forms, serveWebsitePages } from './website-page';

/**
 * Optocht inschrijven (fase 11c, sinds 21d een pagina van de website met inloggen): de echte formulier-HTML en scripts;
 * de API is nagebootst. Zonder login-configuratie gaat de pagina direct naar het formulier voor gasten.
 */
const parade = {
  id: 'p-1',
  name: 'Optocht Loil 2027',
  registrationOpen: true,
  registrationOpensAt: '2026-12-01T09:00:00Z',
  subjectRequired: true,
  infoHtml: '<p>Lees eerst het <strong>reglement</strong>.</p>',
};
const categories = [
  { id: 3, name: 'Volwassenen Loopgroepen groot (10+)', minimumParticipants: 10, maximumParticipants: null },
  { id: 4, name: 'Praalwagens', minimumParticipants: null, maximumParticipants: null },
];

async function serve(
  page: Page,
  api: { bodies: unknown[]; open?: boolean; startStatus?: number },
  overrides: Record<string, string> = {},
) {
  await serveWebsitePages(page, forms.optocht, overrides);
  // Zonder login-configuratie (zoals lokaal): geen inlogkeuze, direct het formulier.
  await page.route('**/api/v1/portal-config', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ clientId: '', authority: '', apiScope: '' }),
    }),
  );
  await page.route('**/api/v1/parade/**', (route) => {
    const url = new URL(route.request().url());
    const json = (data: unknown, status = 200) =>
      route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data) });
    if (url.pathname.endsWith('/current')) return json({ ...parade, registrationOpen: api.open ?? true });
    if (url.pathname.endsWith('/categories')) return json(categories);
    if (url.pathname.endsWith('/status')) {
      return url.searchParams.get('token') === 'goed'
        ? json({
            paradeName: 'Optocht Loil 2027',
            groupName: 'De Bouwers',
            registrationNumber: 7,
            status: 'AdditionalInformationRequired',
            startNumber: null,
            categoryName: 'Praalwagens',
            reason: 'Graag de lengte doorgeven.',
          })
        : json({ title: 'Niet gevonden' }, 404);
    }
    if (url.pathname.endsWith('/verify-email')) return json({ registrationNumber: 7, statusToken: 'goed' });
    api.bodies.push(route.request().postDataJSON());
    if (api.startStatus) {
      return json(
        {
          detail: 'Controleer de ingevulde gegevens.',
          issues: [
            { field: 'adultCount', message: 'Deze categorie is voor minimaal 10 deelnemers.', severity: 'Block' },
          ],
        },
        api.startStatus,
      );
    }
    return json({ id: 'r-1' }, 201);
  });
}

async function fillForm(page: Page) {
  await page.getByLabel('Naam van de groep').fill('De Bouwers');
  await page.getByLabel('Categorie').selectOption({ label: 'Volwassenen Loopgroepen groot (10+)' });
  await expect(page.getByText('Deze categorie is voor minimaal 10 deelnemers.')).toBeVisible();
  await page.getByLabel('Naam', { exact: true }).fill('Piet Test');
  await page.getByLabel('Telefoon', { exact: true }).fill('0612345678');
  await page.getByLabel('E-mailadres', { exact: true }).fill('piet@example.com');
  await page.getByLabel('Onderwerp', { exact: true }).fill('Zwerm bijen');
  await page.getByLabel('Volwassenen', { exact: true }).fill('12');
  await page.getByLabel('Kinderen (tot 16 jaar)').fill('2');
  await page.getByRole('group', { name: 'Hebben jullie muziek bij je?' }).getByLabel('Ja').check();
  const build = page.getByRole('group', { name: 'Bouwlocatie' });
  await build.getByLabel('Straat', { exact: true }).fill('Dorpsstraat');
  await build.getByLabel('Huisnummer').fill('1');
  await build.getByLabel('Postcode').fill('6999 AA');
  await build.getByLabel('Plaats').fill('Loil');
  await page.getByLabel(/Geschatte lengte/).fill('12.5');
  await page.getByLabel(/optochtreglement/).check();
}

test('optocht inschrijven via de webpagina: formulier, code, opgavenummer en statuslink', async ({ page }) => {
  const api = { bodies: [] as unknown[] };
  const errors: string[] = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  await serve(page, api);
  await page.goto('/optocht-inschrijven/');

  await expect(page.getByRole('heading', { name: 'Inschrijven: Optocht Loil 2027' })).toBeVisible();
  await expect(page.getByText('reglement', { exact: true })).toBeVisible();
  await fillForm(page);
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual(
    [],
  );
  await page.getByRole('button', { name: 'Inschrijving versturen' }).click();

  await expect(page.getByRole('heading', { name: 'Bevestig je e-mailadres' })).toBeVisible();
  expect(api.bodies[0]).toMatchObject({
    rulesAccepted: true,
    registration: {
      groupName: 'De Bouwers',
      categoryId: 3,
      adultCount: 12,
      childrenCount: 2,
      hasMusic: true,
      estimatedLengthMeters: 12.5,
      juryInspectionSameAsBuildAddress: true,
      juryInspectionAddress: null,
      buildAddress: { street: 'Dorpsstraat', houseNumber: '1', postalCode: '6999 AA', city: 'Loil' },
    },
  });
  await page.getByLabel('Code', { exact: true }).fill('123456');
  await page.getByRole('button', { name: 'Bevestigen' }).click();
  await expect(page.getByRole('heading', { name: 'Je inschrijving is ingediend' })).toBeVisible();
  await expect(page.getByText('7', { exact: true })).toBeVisible();

  await page.getByRole('link', { name: 'Status van je inschrijving bekijken' }).click();
  await expect(page.getByText('Aanvulling gevraagd')).toBeVisible();
  await expect(page.getByText('Toelichting van de commissie: Graag de lengte doorgeven.')).toBeVisible();
  expect(errors).toEqual([]);
});

test('optocht inschrijven: blokkerende meldingen van de API en een gesloten inschrijving', async ({ page }) => {
  const api = { bodies: [] as unknown[], startStatus: 422 };
  await serve(page, api);
  await page.goto('/optocht-inschrijven/');
  await fillForm(page);
  await page.getByLabel('Volwassenen', { exact: true }).fill('3');
  await page.getByRole('button', { name: 'Inschrijving versturen' }).click();
  await expect(page.getByRole('alert')).toContainText('Deze categorie is voor minimaal 10 deelnemers.');

  const closed = { bodies: [] as unknown[], open: false };
  await page.unrouteAll();
  await serve(page, closed);
  await page.goto('/optocht-inschrijven/');
  await expect(page.getByRole('heading', { name: 'Inschrijven nog niet mogelijk' })).toBeVisible();
  await expect(page.getByText(/opent op/)).toBeVisible();

  await page.goto('/optocht-inschrijven/?status=fout');
  await expect(page.getByText('Deze statuslink is niet (meer) geldig.')).toBeVisible();
});

// ----- Fase 21d: inloggen op de website -------------------------------------------------------------------------------

/** Nagebootste login (de echte MSAL-redirect naar Entra kan hier niet): wel of niet ingelogd. */
function fakeLogin(account: { name: string; username: string } | null) {
  return {
    'js/login.js': `window.DrammersLogin = (() => {
      const state = { available: true, account: ${JSON.stringify(account)} };
      return {
        state,
        async init() { return state; },
        signIn() { window.__signIn = true; },
        signOut() { window.__signOut = true; },
        fetch: (url, o = {}) => fetch(url, { ...o, headers: { ...(o.headers ?? {}), authorization: 'Bearer test' } }),
      };
    })();`,
  };
}

const draft = {
  id: 'd-1',
  version: 'v1',
  status: 'Draft',
  groupName: 'De Bouwers',
  contactName: 'Piet Test',
  contactPhone: '+31612345678',
  contactPhoneDisplay: '06 12345678',
  contactEmail: 'piet@example.com',
  categoryId: null,
  adultCount: 0,
  childrenCount: 0,
  buildAddress: {
    street: 'Dorpsstraat',
    houseNumber: '1',
    addition: null,
    postalCode: '6999 AA',
    city: 'Loil',
    country: 'NL',
  },
  juryInspectionSameAsBuildAddress: true,
  juryInspectionAddress: {
    street: null,
    houseNumber: null,
    addition: null,
    postalCode: null,
    city: null,
    country: null,
  },
};

async function serveSignedIn(page: Page, permissions: string[], earlier: unknown[] = []) {
  const calls: { method: string; path: string; body: unknown; auth: string | undefined }[] = [];
  let mine: unknown[] = earlier;
  await serve(page, { bodies: [] }, fakeLogin({ name: 'Piet Test', username: 'piet@example.com' }));
  await page.route('**/api/v1/me', (route) =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify({ displayName: 'Piet Test', permissions }) }),
  );
  await page.route('**/api/v1/parade/registrations**', (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    calls.push({
      method: request.method(),
      path,
      body: request.postDataJSON(),
      auth: request.headers()['authorization'],
    });
    const json = (data: unknown, status = 200) =>
      route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data) });
    if (path.endsWith('/submit')) {
      mine = [
        {
          id: 'd-1',
          groupName: 'De Bouwers',
          status: 'Submitted',
          registrationNumber: 12,
          startNumber: null,
          paradeId: 'p-1',
          paradeName: 'Optocht Loil 2027',
        },
      ];
      return json({ ...draft, status: 'Submitted', registrationNumber: 12 });
    }
    if (request.method() === 'PUT') return json({ ...draft, ...(request.postDataJSON() as object), version: 'v2' });
    if (request.method() === 'POST') return json(draft, 201);
    return json(mine);
  });
  return calls;
}

test('fase 21d: niet ingelogd eerst de keuze tussen inloggen en zonder account', async ({ page }) => {
  await serve(page, { bodies: [] }, fakeLogin(null));
  await page.goto('/optocht-inschrijven/');
  await expect(page.getByRole('heading', { name: 'Inloggen en inschrijven' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Zonder account inschrijven' })).toBeVisible();
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual(
    [],
  );
  await page.getByRole('button', { name: 'Inloggen' }).click();
  expect(await page.evaluate(() => (window as unknown as { __signIn?: boolean }).__signIn)).toBe(true);
  await page.getByRole('button', { name: 'Zonder account verder' }).click();
  await expect(page.getByRole('heading', { name: 'Inschrijfformulier' })).toBeVisible();
  await expect(page.getByText(/krijg je een code per e-mail/)).toBeVisible();
});

test('fase 21d: ingelogd inschrijven zonder e-mailcode en daarna Mijn inschrijving', async ({ page }) => {
  const calls = await serveSignedIn(page, ['parade.register']);
  await page.goto('/optocht-inschrijven/');
  await expect(page.getByText('Ingelogd als Piet Test')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Mijn inschrijving' })).toBeVisible();
  await expect(page.getByText('Je hebt nog geen inschrijving voor de optocht.')).toBeVisible();

  await page.getByRole('button', { name: 'Nieuwe inschrijving' }).click();
  await expect(page.getByRole('heading', { name: 'Inschrijfformulier' })).toBeVisible();
  // Vooraf ingevuld uit het concept (gegevens van het lid en de vorige bouwlocatie).
  await expect(page.getByLabel('Naam van de groep')).toHaveValue('De Bouwers');
  await expect(page.getByRole('group', { name: 'Bouwlocatie' }).getByLabel('Straat', { exact: true })).toHaveValue(
    'Dorpsstraat',
  );
  await expect(page.getByText(/je inschrijving direct ingediend/)).toBeVisible();
  await fillForm(page);
  await page.getByRole('button', { name: 'Inschrijving versturen' }).click();

  await expect(page.getByRole('heading', { name: 'Je inschrijving is ingediend' })).toBeVisible();
  await expect(page.getByText('12', { exact: true })).toBeVisible();
  expect(calls.map((c) => `${c.method} ${c.path}`)).toEqual([
    'GET /api/v1/parade/registrations',
    'POST /api/v1/parade/registrations',
    'PUT /api/v1/parade/registrations/d-1',
    'POST /api/v1/parade/registrations/d-1/submit',
  ]);
  expect(calls.every((c) => c.auth === 'Bearer test')).toBe(true);
  expect(calls[2]!.body).toMatchObject({ version: 'v1', groupName: 'De Bouwers', categoryId: 3, adultCount: 12 });

  await page.getByRole('button', { name: 'Naar Mijn inschrijving' }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'De Bouwers' })).toContainText('Ingediend');
  await expect(page.getByRole('listitem').filter({ hasText: 'De Bouwers' })).toContainText('Opgavenummer 12');
  await expect(page.getByRole('button', { name: 'Nieuwe inschrijving' })).toBeHidden();
  await page.getByRole('button', { name: 'Uitloggen' }).click();
  expect(await page.evaluate(() => (window as unknown as { __signOut?: boolean }).__signOut)).toBe(true);
});

test('fase 21d: ingelogd zonder rechten als groepsverantwoordelijke', async ({ page }) => {
  await serveSignedIn(page, []);
  await page.goto('/optocht-inschrijven/');
  await expect(page.getByRole('heading', { name: 'Een groep inschrijven' })).toBeVisible();
  await expect(page.getByText(/groepsverantwoordelijke/)).toBeVisible();
  await page.getByRole('button', { name: 'Zonder account inschrijven' }).click();
  await expect(page.getByRole('heading', { name: 'Inschrijfformulier' })).toBeVisible();
});

test('nieuwe optocht: eerdere inschrijving staat apart, opnieuw inschrijven met dezelfde of een andere bouwlocatie', async ({
  page,
}) => {
  await serveSignedIn(
    page,
    ['parade.register'],
    [
      {
        id: 'r-old',
        groupName: 'De Bouwers',
        status: 'StartNumberAssigned',
        registrationNumber: 12,
        startNumber: 31,
        paradeId: 'p-0',
        paradeName: 'Optocht Loil 2026',
      },
    ],
  );
  await page.goto('/optocht-inschrijven/');
  await expect(page.getByText('Je hebt nog geen inschrijving voor de optocht.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Eerdere optochten' })).toBeVisible();
  await expect(page.getByText('Optocht Loil 2026: De Bouwers')).toBeVisible();

  await page.getByRole('button', { name: 'Nieuwe inschrijving' }).click();
  const build = page.getByRole('group', { name: 'Bouwlocatie' });
  await expect(build.getByText('Dorpsstraat 1, Loil')).toBeVisible();
  await expect(build.getByLabel('Straat', { exact: true })).toHaveValue('Dorpsstraat');
  await build.getByLabel(/Andere locatie/).check();
  await expect(build.getByLabel('Straat', { exact: true })).toHaveValue('');
  await build.getByLabel(/Zelfde locatie/).check();
  await expect(build.getByLabel('Straat', { exact: true })).toHaveValue('Dorpsstraat');
});
