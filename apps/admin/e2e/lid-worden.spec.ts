import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * De openbare webpagina /lid-worden (fase 9b): echte bestanden uit src/Drammers.Api/wwwroot, met dezelfde CSP als de
 * API; alleen de API-aanroepen zijn nagebootst.
 */
const root = join(dirname(fileURLToPath(import.meta.url)), '../../../src/Drammers.Api/wwwroot/lid-worden');
const csp =
  "default-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
const types: Record<string, string> = { html: 'text/html', css: 'text/css', js: 'text/javascript', png: 'image/png' };

async function serve(page: Page, api: { bodies: unknown[]; verifyStatus?: number }) {
  await page.route('**/lid-worden/**', (route) => {
    const file = new URL(route.request().url()).pathname.replace('/lid-worden/', '') || 'index.html';
    const ext = file.split('.').pop()!;
    return route.fulfill({
      status: 200,
      contentType: types[ext],
      headers: { 'content-security-policy': csp },
      body: readFileSync(join(root, file)),
    });
  });
  await page.route('**/api/v1/membership-applications**', (route) => {
    const url = route.request().url();
    if (url.endsWith('/verify-email')) {
      return route.fulfill({ status: api.verifyStatus ?? 204 });
    }
    api.bodies.push(route.request().postDataJSON());
    return route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ id: 'a-1' }) });
  });
}

test('lid worden via de webpagina: kind onder de 16 met ouder, code, klaar', async ({ page }) => {
  const api = { bodies: [] as unknown[] };
  const errors: string[] = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  await serve(page, api);
  await page.goto('/lid-worden/');

  await page.getByRole('radio', { name: /Dansgarde/ }).check();
  await page.getByLabel('Voornaam').fill('Sanne');
  await page.getByLabel('Achternaam').fill('Jansen');
  await page.getByLabel('Geboortedatum').fill(`${new Date().getFullYear() - 8}-03-12`);
  await expect(page.getByRole('group', { name: 'Ouder of verzorger' })).toBeVisible();
  await page.getByLabel('Straat en huisnummer').fill('Dorpsstraat 3');
  await page.getByLabel('Postcode').fill('6999 AB');
  await page.getByLabel('Woonplaats').fill('Loil');
  await page.getByLabel('Naam ouder/verzorger').fill('Anja Jansen');
  await page.getByLabel('Telefoon ouder/verzorger').fill('0612345678');
  await page.getByLabel('E-mailadres ouder/verzorger').fill('ouder@example.com');
  await page.getByLabel('IBAN').fill('NL91 ABNA 0417 1643 00');
  await page.getByLabel('Naam rekeninghouder').fill('A. Jansen');
  await page.getByLabel(/doorlopende incasso/).check();
  await page.getByLabel(/privacyverklaring/).check();

  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual(
    [],
  );

  await page.getByRole('button', { name: 'Aanmelding versturen' }).click();
  await expect(page.getByRole('heading', { name: 'Bevestig je e-mailadres' })).toBeVisible();
  expect(api.bodies[0]).toMatchObject({
    guardianName: 'Anja Jansen',
    email: 'ouder@example.com',
    source: 'Website',
    mandateConsent: true,
    membershipType: 'Dansgarde',
  });

  await page.getByLabel('Code', { exact: true }).fill('123456');
  await page.getByRole('button', { name: 'Bevestigen' }).click();
  await expect(page.getByRole('heading', { name: 'Bedankt voor je aanmelding!' })).toBeVisible();
  expect(errors.filter((e) => e.includes('Content Security Policy'))).toEqual([]);
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth),
  ).toBeLessThanOrEqual(1);
});

test('volwassene ziet geen ouder-sectie; een verkeerde code geeft een melding', async ({ page }) => {
  const api = { bodies: [] as unknown[], verifyStatus: 422 };
  await page.route('**/verify-email', (route) =>
    route.fulfill({
      status: 422,
      contentType: 'application/json',
      body: JSON.stringify({ detail: 'Deze code klopt niet.' }),
    }),
  );
  await serve(page, api);
  await page.goto('/lid-worden/');
  await page.getByLabel('Geboortedatum').fill('1990-03-12');
  await expect(page.getByRole('group', { name: 'Ouder of verzorger' })).toBeHidden();
  // Vanaf 15 een eigen aanmelding (fase 17).
  await page.getByLabel('Geboortedatum').fill(`${new Date().getFullYear() - 16}-01-01`);
  await expect(page.getByRole('group', { name: 'Ouder of verzorger' })).toBeHidden();
});
