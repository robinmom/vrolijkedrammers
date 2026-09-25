import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

async function open(page: Page, api: MockApi, path = '') {
  await api.install(page);
  await page.goto(`/beheer/${path}`);
}

async function openMenuIfMobile(page: Page) {
  const toggle = page.getByRole('button', { name: 'Menu' });
  if (await toggle.isVisible()) {
    await toggle.click();
  }
}

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(serious.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)).toEqual([]);
}

test('bestuurder kent een rol toe en ziet die in de auditlog', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'gebruikers');
  await page.getByRole('link', { name: 'Jan Lid' }).click();
  await page.getByRole('checkbox', { name: 'Redactie' }).check();
  await page.getByRole('button', { name: 'Rollen opslaan' }).click();
  await expect(page.getByRole('status')).toHaveText('Rollen opgeslagen.');

  await openMenuIfMobile(page);
  await page.getByRole('link', { name: 'Auditlog' }).click();
  await expect(page.getByRole('cell', { name: 'user.roles.changed' })).toBeVisible();
  expect(api.userRoles['u-jan']!.map((r) => r.code)).toEqual(['lid', 'redactie']);
});

test('gebruiker zonder beheerrechten ziet "Geen beheerrechten"', async ({ page }) => {
  await open(page, new MockApi(['news.read']));
  await expect(page.getByRole('heading', { name: 'Geen beheerrechten' })).toBeVisible();
  await openMenuIfMobile(page);
  await expect(page.getByRole('navigation', { name: 'Navigatie' }).getByRole('link')).toHaveCount(0);
});

test('onderhoudsmodus aanzetten', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'configuratie');
  await page.getByRole('checkbox', { name: /Onderhoudsmodus/ }).check();
  await page.getByLabel('Onderhoudsmelding').fill('We zijn zo terug');
  await page.getByRole('region', { name: 'App en onderhoud' }).getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('App-instellingen opgeslagen; direct actief.')).toBeVisible();
  expect(api.appConfig.maintenanceMode).toBe(true);
});

test('carnavalsjaar toevoegen en activeren; precies één actief', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'carnavalsjaren');
  await page.getByRole('button', { name: 'Carnavalsjaar toevoegen' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Naam').fill('2027/2028');
  await dialog.getByLabel('Seizoen vanaf').fill('2027-11-11');
  await dialog.getByLabel('Seizoen tot').fill('2028-03-01');
  await dialog.getByLabel('Carnaval vanaf').fill('2028-02-26');
  await dialog.getByLabel('Carnaval tot').fill('2028-02-29');
  await dialog.getByRole('button', { name: 'Opslaan' }).click();
  await page.getByRole('button', { name: 'Activeren 2027/2028' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Activeren' }).click();
  await expect(page.getByText('2027/2028 is nu actief.')).toBeVisible();
  expect(api.years.filter((y) => y.active).map((y) => y.name)).toEqual(['2027/2028']);
});

test('redacteur publiceert een event voor iedereen; het staat in de publieke agenda', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'agenda');
  await page.getByRole('link', { name: 'Event toevoegen' }).click();
  await page.getByLabel('Titel').fill('Pronkzitting');
  await page.getByLabel('Begint').fill('2027-01-16T20:00');
  await page.getByLabel('Status').selectOption('Published');
  await page.getByRole('button', { name: 'Opslaan', exact: true }).click();
  await expect(page.getByText('Event opgeslagen.')).toBeVisible();

  const agenda = await page.evaluate(async () => (await fetch('/api/v1/events')).json());
  expect(agenda.items.map((e: { title: string }) => e.title)).toEqual(['Pronkzitting']);
  expect(api.events[0]!.publication.visibility).toBe('Public');
});

for (const path of [
  '',
  'agenda',
  'agenda/nieuw',
  'nieuws',
  'nieuws/nieuw',
  'fotos',
  'fotos/a-1',
  'gebruikers',
  'gebruikers/u-jan',
  'rollen',
  'carnavalsjaren',
  'configuratie',
  'auditlog',
]) {
  test(`toegankelijkheid (axe) /${path}`, async ({ page }) => {
    await open(page, new MockApi(), path);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expectNoSeriousA11yIssues(page);
  });
}
