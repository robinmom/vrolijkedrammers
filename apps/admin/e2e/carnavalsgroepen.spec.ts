import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/**
 * Carnavalsgroepen (2026-11-17): het register onder Optocht. Een groep blijft over de jaren bestaan, en zodra ze zich
 * inschrijven staan de gegevens van die inschrijving (adres, contactpersoon, aantallen) bij de groep.
 */

const PERMISSIONS = ['parade.read', 'parade.manage', 'member.read'];

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);
}

test('het overzicht laat zien sinds wanneer een groep meedoet en wie dit jaar is ingeschreven', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/optocht/groepen');

  const row = page.getByRole('row', { name: /CV de Kuitenbijters/ });
  await expect(row).toContainText('1998');
  await expect(row).toContainText('Ingeschreven');
  // Het laatste jaar met wagen en plaats.
  await expect(row).toContainText('2027');
  await expect(page.getByRole('row', { name: /De Loopse Lopers/ })).toContainText('onbekend');
  await expectNoSeriousA11yIssues(page);
});

test('een inschrijving met een andere naam koppelen aan de voorgestelde groep', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/optocht/groepen');

  const card = page.getByRole('region', { name: 'Nog koppelen (1)' });
  await expect(card).toContainText('Kuitenbijters');
  await card.getByRole('button', { name: 'Koppelen aan CV de Kuitenbijters' }).click();

  await expect(page.getByRole('status')).toContainText('Gekoppeld aan CV de Kuitenbijters');
  expect(api.unlinkedRegistrations).toEqual([]);
});

test('bij de groep staan de gegevens van de inschrijving van dit jaar', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/optocht/groepen/cg-1');

  await expect(page.getByRole('heading', { name: 'CV de Kuitenbijters', level: 1 })).toBeVisible();
  await expect(page.getByText(/Doet mee sinds 1998/)).toBeVisible();

  // Het jaar zonder eigen antwoord leent het onderwerp van de inschrijving.
  const jaar2027 = page.getByRole('region', { name: /2027 · Loil in de ruimte/ });
  await expect(jaar2027).toContainText('Bosweg 3, 6999 AA Loil');
  await expect(jaar2027).toContainText('Jan Jansen');
  await expect(jaar2027).toContainText('12 volwassenen, 4 kinderen');
  await expect(jaar2027).toContainText('Opgavenummer 3');

  // Het oude jaar met de plaats uit de uitslag.
  await expect(page.getByRole('region', { name: /2026 · Drammers in de rondte/ })).toContainText(
    '2e plaats (uit de uitslag)',
  );
  await expectNoSeriousA11yIssues(page);
});

test('een jaar van vóór de app zelf toevoegen', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/optocht/groepen/cg-1');

  await page.getByRole('button', { name: 'Jaar toevoegen' }).click();
  await page.getByRole('spinbutton', { name: 'Jaar' }).fill('2009');
  await page.getByLabel('Naam van de wagen of loopgroep').fill('Drammers op de maan');
  await page.getByLabel('Categorie').selectOption('1');
  await page.getByLabel('Plaats in de uitslag').fill('1');
  await page.getByRole('button', { name: 'Opslaan' }).click();

  await expect(page.getByRole('region', { name: /2009 · Drammers op de maan/ })).toContainText('1e plaats');
  expect(api.paradeGroups[0]!.years.find((y) => y.year === 2009)).toMatchObject({
    floatName: 'Drammers op de maan',
    categoryId: 1,
    place: 1,
  });
});

test('op een smal scherm loopt niets buiten beeld', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/optocht/groepen/cg-1');

  await expect(page.getByRole('heading', { name: 'CV de Kuitenbijters', level: 1 })).toBeVisible();
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(1);
});
