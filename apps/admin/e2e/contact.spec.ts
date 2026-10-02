import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { forms, serveWebsitePages } from './website-page';

/** Fase 21i: het contactformulier van de website met het echte script; de API is nagebootst. */

async function serve(page: Page, api: { bodies: Record<string, unknown>[]; status?: number }) {
  await serveWebsitePages(page, forms.contact);
  await page.route('**/api/v1/contact', (route) => {
    if (route.request().method() === 'GET') {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          recipients: [
            { key: 'secretariaat', label: 'Ledenadministratie' },
            { key: 'optocht', label: 'Optocht' },
            { key: 'penningmeester', label: 'Kaarten en betalingen' },
          ],
          turnstileSiteKey: null,
        }),
      });
    }
    api.bodies.push(route.request().postDataJSON());
    return route.fulfill({ status: api.status ?? 202 });
  });
}

test('contactformulier: ontvanger uit de link, versturen en bedankt', async ({ page }) => {
  const api = { bodies: [] as Record<string, unknown>[] };
  const errors: string[] = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  await serve(page, api);
  await page.goto('/contact/?aan=optocht');

  await expect(page.getByLabel('Waar gaat je vraag over?')).toHaveValue('optocht');
  await page.getByLabel('Naam', { exact: true }).fill('Jan de Bouwer');
  await page.getByLabel('E-mail', { exact: true }).fill('jan@example.com');
  await page.getByLabel('Bericht', { exact: true }).fill('Mag onze wagen 4 meter hoog zijn?');
  // Het verborgen veld voor bots zit niet in de toegankelijkheidsboom.
  await expect(page.getByRole('textbox', { name: 'Laat dit veld leeg' })).toHaveCount(0);

  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual(
    [],
  );

  await page.getByRole('button', { name: 'Versturen' }).click();
  await expect(page.getByRole('heading', { name: 'Bedankt voor je bericht!' })).toBeVisible();
  expect(api.bodies[0]).toMatchObject({
    recipient: 'optocht',
    name: 'Jan de Bouwer',
    email: 'jan@example.com',
    phone: null,
    message: 'Mag onze wagen 4 meter hoog zijn?',
    website: null,
    turnstileToken: null,
  });
  expect(typeof api.bodies[0]!.elapsedMs).toBe('number');
  expect(errors.filter((e) => e.includes('Content Security Policy'))).toEqual([]);
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth),
  ).toBeLessThanOrEqual(1);
});

test('contactformulier: verplichte velden en te veel berichten', async ({ page }) => {
  const api = { bodies: [] as Record<string, unknown>[], status: 429 };
  await serve(page, api);
  await page.goto('/contact/');
  await expect(page.getByLabel('Waar gaat je vraag over?')).toHaveValue('secretariaat');

  await page.getByRole('button', { name: 'Versturen' }).click();
  expect(api.bodies).toHaveLength(0);

  await page.getByLabel('Naam', { exact: true }).fill('Anna');
  await page.getByLabel('E-mail', { exact: true }).fill('anna@example.com');
  await page.getByLabel('Bericht', { exact: true }).fill('Hoi!');
  await page.getByRole('button', { name: 'Versturen' }).click();
  await expect(page.getByRole('alert')).toHaveText(/Probeer het over tien minuten opnieuw/);
  await expect(page.getByRole('heading', { name: 'Stuur ons een bericht' })).toBeVisible();
});
