import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/**
 * Bestuur en raad van elf (2026-11-17): wie zit erin en hoe lang al, met een balk waarin elke drie jaar (één
 * bestuursperiode) een streep staat. Een periode kan met één of twee jaar verlengd worden; de raad van elf heeft geen
 * periode.
 */

const PERMISSIONS = ['member.read', 'member.update'];

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);
}

test('het bestuur met de balk per persoon en het einde van de periode', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/bestuur');

  await expect(page.getByRole('heading', { name: 'Bestuur en raad van elf', level: 1 })).toBeVisible();
  await expect(page.getByText(/elke streep 3 jaar/)).toBeVisible();

  // De voorzitter zit er sinds 2019 in: dat is zijn derde periode.
  const voorzitter = page.getByRole('row', { name: /Test Bestuurder/ });
  await expect(voorzitter).toContainText('Voorzitter');
  await expect(voorzitter).toContainText('periode 3');
  await expect(voorzitter.getByRole('img', { name: /jaar in het bestuur/ })).toBeVisible();

  // De raad van elf heeft geen periode die afloopt.
  await page.getByRole('button', { name: 'Raad van Elf' }).click();
  await expect(page.getByRole('row', { name: /Raadslid Riet/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /verlengen voor/ })).toHaveCount(0);
  await expectNoSeriousA11yIssues(page);
});

test('een periode met twee jaar verlengen', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/bestuur');

  // De knoppen staan alleen bij een periode die (bijna) afloopt; de voorzitter zit nog middenin de zijne.
  await expect(
    page.getByRole('row', { name: /Test Bestuurder/ }).getByRole('button', { name: /\+2 jaar/ }),
  ).toHaveCount(0);
  await page
    .getByRole('row', { name: /Nieuwe Penningmeester/ })
    .getByRole('button', { name: /\+2 jaar/ })
    .click();

  await expect(page.getByRole('status')).toContainText('met 2 jaar verlengd');
  await expect(page.getByRole('row', { name: /Nieuwe Penningmeester/ })).toContainText('verlengd met 2 jaar');
  expect(api.boardSeats.find((s) => s.id === 'b-2')!.terms.at(-1)!.extensionYears).toBe(2);
});

test('een nieuw raadslid toevoegen zonder periodes', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/bestuur');

  await page.getByRole('button', { name: 'Toevoegen' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Orgaan').selectOption('CouncilOfEleven');
  await expect(page.getByRole('group', { name: 'Bestuursperiodes' })).toHaveCount(0);
  await page.getByLabel('Of een naam zelf').fill('Nieuw Raadslid');
  await page.getByLabel('In functie sinds').fill('2026-11-11');
  await page.getByRole('button', { name: 'Opslaan' }).click();

  expect(api.boardSeats.at(-1)).toMatchObject({ body: 'CouncilOfEleven', name: 'Nieuw Raadslid', terms: [] });
});

test('op een smal scherm loopt niets buiten beeld', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/bestuur');

  await expect(page.getByRole('heading', { name: 'Bestuur en raad van elf', level: 1 })).toBeVisible();
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(1);
});
