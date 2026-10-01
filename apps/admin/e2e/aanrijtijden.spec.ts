import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { forms, serveWebsitePages } from './website-page';
import { MockApi } from './mock-api';

/** Fase 16: aanrijtijden in het portal en de openbare webpagina /aanrijtijden. */

async function open(page: Page, api: MockApi, path = '') {
  await api.install(page);
  await page.goto(`/beheer/${path}`);
}

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(serious.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)).toEqual([]);
}

test('fase 16: aanrijtijden genereren, aanpassen, meldplek en publiceren', async ({ page }) => {
  const api = new MockApi(['parade.read', 'parade.import-arrival-times']);
  await open(page, api, 'optocht/aanrijtijden');
  await expect(page.getByRole('heading', { name: 'Aanrijtijden', level: 1 })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Publiceren' })).toBeDisabled();
  await expectNoSeriousA11yIssues(page);

  await page.getByLabel('Eerste aanrijtijd').fill('10:30');
  await page.getByLabel('Minuten per wagen').fill('4');
  await page.getByRole('button', { name: 'Tijden genereren' }).click();
  await expect(page.getByText('2 aanrijtijd(en) ingevuld.')).toBeVisible();
  await expect(page.getByLabel('Aanrijtijd De Sökkels')).toHaveValue('10:34');

  await page.getByLabel('Aanrijtijd De Sökkels').fill('10:40');
  await page.getByLabel('Aanrijtijd De Sökkels').blur();
  await expect
    .poll(() => api.lineupCalls.at(-1))
    .toMatchObject({ path: '/admin/parade/arrival-times/r-7', body: { arrivalTime: '10:40:00' } });

  await page.getByRole('textbox', { name: 'Meldplek' }).fill('Parkeerplaats Loil');
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Meldplek opgeslagen.')).toBeVisible();

  await page.getByRole('button', { name: 'Publiceren (2)' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Publiceren' }).click();
  await expect(page.getByText('Gepubliceerd: 2 groep(en) krijgen een melding.')).toBeVisible();
  await expect(page.getByRole('link', { name: 'openbare pagina' })).toHaveAttribute('href', '/aanrijtijden/');
});

// Fase 21d: de openbare lijst is een pagina van de website (echte formulier-HTML en script, API nagebootst).
async function serve(page: Page, published: boolean) {
  await serveWebsitePages(page, forms.aanrijtijden);
  await page.route('**/api/v1/parade/arrival-times', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        published,
        paradeName: 'Optocht 2027',
        paradeDate: '2027-02-07',
        location: 'Rotonde Holthuizen',
        rows: published
          ? [
              { startNumber: 5, category: 'Getrokken wagens', groupName: 'De Snotapen', arrivalTime: '10:30' },
              {
                startNumber: 7,
                category: 'Zelfrijdend voertuigen',
                groupName: '<b>De Sökkels</b>',
                arrivalTime: '10:34',
              },
            ]
          : [],
      }),
    }),
  );
}

test('fase 16: openbare webpagina met de aanrijtijden, zoals de tabel op de website', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  await serve(page, true);
  await page.goto('/aanrijtijden/');
  await expect(page.getByRole('heading', { name: 'Aanrijtijden Optocht 2027' })).toBeVisible();
  await expect(page.getByRole('columnheader', { name: 'Rotonde Holthuizen' })).toBeVisible();
  await expect(page.getByRole('row', { name: /5 Getrokken wagens De Snotapen 10:30 uur/ })).toBeVisible();
  // Namen zijn tekst, geen HTML.
  await expect(page.getByRole('cell', { name: '<b>De Sökkels</b>' })).toBeVisible();
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual(
    [],
  );
  expect(errors.filter((e) => e.includes('Content Security Policy'))).toEqual([]);
});

test('fase 16: openbare webpagina voordat de tijden bekend zijn', async ({ page }) => {
  await serve(page, false);
  await page.goto('/aanrijtijden/');
  await expect(page.getByText('De aanrijtijden zijn nog niet bekend.')).toBeVisible();
  await expect(page.getByRole('table')).toBeHidden();
});
