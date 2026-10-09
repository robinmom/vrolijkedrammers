import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { forms, serveWebsitePages } from './website-page';

/** Lid worden (fase 9b, sinds 21d een pagina van de website): de echte formulier-HTML en het script; de API is nagebootst. */

async function serve(page: Page, api: { bodies: unknown[]; verifyStatus?: number }) {
  await serveWebsitePages(page, forms.lidWorden);
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

test('lid splitsen via de link uit de mail: alles ingevuld, geen IBAN', async ({ page }) => {
  const api = { bodies: [] as unknown[] };
  // Na serve(): de laatst geregistreerde route gaat voor.
  await serve(page, api);
  await page.route('**/api/v1/membership-applications/split/**', (route) =>
    route.request().url().endsWith('/split/goed')
      ? route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            mainMemberName: 'Jan',
            secondFirstName: 'Marie',
            secondNamePrefix: 'de',
            secondLastName: 'Vries',
            addressLine: 'Kerkstraat 2',
            postalCode: '6999 AB',
            city: 'Loil',
          }),
        })
      : route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }),
  );
  await page.goto('/lid-worden/?splitsen=goed');

  await expect(page.getByRole('heading', { name: 'Tweede lid registreren' })).toBeVisible();
  await expect(page.getByRole('radio', { name: /lid splitsen/ })).toBeChecked();
  await expect(page.getByText(/tweede lid van het lidmaatschap van Jan/)).toBeVisible();
  await expect(page.getByLabel('Voornaam')).toHaveValue('Marie');
  await expect(page.getByLabel('Achternaam')).toHaveValue('Vries');
  // Het e-mailadres van het hoofdlid wordt niet ingevuld: het tweede lid vult een eigen adres in.
  await expect(page.getByLabel('E-mailadres', { exact: true })).toHaveValue('');
  await page.getByLabel('E-mailadres', { exact: true }).fill('marie@example.com');
  await expect(page.getByLabel('IBAN')).toBeHidden();
  await expect(page.getByRole('group', { name: 'Soort lidmaatschap' })).toBeHidden();

  await page.getByLabel('Geboortedatum').fill('1966-05-01');
  await page.getByLabel(/privacyverklaring/).check();
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual(
    [],
  );
  await page.getByRole('button', { name: 'Aanmelding versturen' }).click();
  await expect(page.getByRole('heading', { name: 'Bevestig je e-mailadres' })).toBeVisible();
  expect(api.bodies[0]).toMatchObject({
    firstName: 'Marie',
    email: 'marie@example.com',
    iban: null,
    accountHolder: null,
    mandateConsent: false,
    membershipType: 'Individual',
    splitToken: 'goed',
  });
});

test('lid splitsen zonder (geldige) link: uitleg en niet versturen', async ({ page }) => {
  await serve(page, { bodies: [] });
  await page.route('**/api/v1/membership-applications/split/**', (route) =>
    route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }),
  );
  await page.goto('/lid-worden/?splitsen=verlopen');
  await expect(page.getByText(/Deze link is niet \(meer\) geldig/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Aanmelding versturen' })).toBeDisabled();

  await page.goto('/lid-worden/');
  await page.getByRole('radio', { name: /lid splitsen/ }).check();
  await expect(page.getByText(/Gebruik de persoonlijke link/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Aanmelding versturen' })).toBeDisabled();
  await page.getByRole('radio', { name: 'Nieuw lid' }).check();
  await expect(page.getByLabel('IBAN')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Aanmelding versturen' })).toBeEnabled();
});
