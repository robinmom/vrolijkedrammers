import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 19t: tafelindeling pronkzitting — plattegrond (4 rijen × tafels van 8), verplaatsen, tafels per rij en afdruklijst. */

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);
}

test('plattegrond met podium, vier rijen en tafels van acht', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await api.install(page);
  await page.goto('/beheer/verkoop/pronkzitting/tafelindeling');

  await expect(page.getByRole('heading', { name: 'Tafelindeling pronkzitting', level: 1 })).toBeVisible();
  await expect(page.getByText('PODIUM', { exact: true })).toBeVisible();
  await expect(page.getByText('18 plaatsen', { exact: false })).toBeVisible();
  for (const rij of ['Rij 1', 'Rij 2', 'Rij 3', 'Rij 4']) {
    await expect(page.getByText(rij, { exact: true }).first()).toBeVisible();
  }

  // De grootste groep (10 personen) staat op twee tafels in rij 1.
  await expect(page.getByLabel('Tafel 101')).toBeVisible();
  await expect(page.getByLabel('Tafel 102')).toBeVisible();

  // De gezelschappen staan in de afdruklijst met hun tafels.
  for (const name of ['De Kruumels', 'Fam. Bos', 'Jan Jansen']) {
    await expect(page.getByRole('cell', { name, exact: false }).first()).toBeVisible();
  }
  await expectNoSeriousA11yIssues(page);
});

test('een gezelschap naar een andere rij verplaatsen', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await api.install(page);
  await page.goto('/beheer/verkoop/pronkzitting/tafelindeling');

  const rijSelect = page.getByLabel('Rij voor Jan Jansen');
  await expect(rijSelect).toHaveValue('2');
  await rijSelect.selectOption('0');

  // Na het verplaatsen zit Jan Jansen in rij 1.
  await expect(page.getByLabel('Rij voor Jan Jansen')).toHaveValue('0');
  expect(api.seatingState['sp-vr']?.assignments['O:ord-12']?.row).toBe(0);
});

test('tafels per rij instellen, automatisch verdelen en de afdruklijst downloaden', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await api.install(page);
  await page.goto('/beheer/verkoop/pronkzitting/tafelindeling');

  await page.getByLabel('Tafels per rij').fill('4');
  await page.getByLabel('Tafels per rij').blur();
  await expect(page.getByText('Aantal tafels per rij opgeslagen.')).toBeVisible();
  expect(api.seatingState['sp-vr']?.tablesPerRow).toBe(4);

  // Eerst iets verplaatsen, dan automatisch verdelen wist de handmatige plaatsen.
  await page.getByLabel('Rij voor Jan Jansen').selectOption('3');
  await page.getByRole('button', { name: 'Automatisch verdelen' }).click();
  await expect(page.getByText('De indeling is automatisch verdeeld.')).toBeVisible();
  expect(Object.keys(api.seatingState['sp-vr']?.assignments ?? {})).toHaveLength(0);

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Afdruklijst (Excel)' }).click();
  expect((await download).suggestedFilename()).toBe('Pronkzitting afdruklijst 5-2-2027.xlsx');
});
