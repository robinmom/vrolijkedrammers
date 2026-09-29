import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * De openbare webpagina /optocht-inschrijven (fase 11c): echte bestanden uit src/Drammers.Api/wwwroot, met dezelfde CSP
 * als de API; alleen de API-aanroepen zijn nagebootst.
 */
const root = join(dirname(fileURLToPath(import.meta.url)), '../../../src/Drammers.Api/wwwroot/optocht-inschrijven');
const csp =
  "default-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
const types: Record<string, string> = { html: 'text/html', css: 'text/css', js: 'text/javascript', png: 'image/png' };

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

async function serve(page: Page, api: { bodies: unknown[]; open?: boolean; startStatus?: number }) {
  await page.route('**/optocht-inschrijven/**', (route) => {
    const file = new URL(route.request().url()).pathname.replace('/optocht-inschrijven/', '') || 'index.html';
    const ext = file.split('.').pop()!;
    return route.fulfill({
      status: 200,
      contentType: types[ext],
      headers: { 'content-security-policy': csp },
      body: readFileSync(join(root, file)),
    });
  });
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
  await build.getByLabel('Straat').fill('Dorpsstraat');
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
