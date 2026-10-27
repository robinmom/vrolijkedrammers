import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 19t: tafelindeling pronkzitting — plattegrond (4 rijen naast elkaar × tafels van 8), vullen, verplaatsen en afdruklijst. */

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);
}

test('plattegrond met podium bovenaan en vier rijen naast elkaar', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await api.install(page);
  await page.goto('/beheer/verkoop/pronkzitting/tafelindeling');

  await expect(page.getByRole('heading', { name: 'Tafelindeling pronkzitting', level: 1 })).toBeVisible();
  await expect(page.getByText('PODIUM', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '18 plaatsen', exact: false })).toBeVisible();
  for (const rij of ['Rij 1', 'Rij 2', 'Rij 3', 'Rij 4']) {
    await expect(page.getByText(rij, { exact: true }).first()).toBeVisible();
  }

  // De grootste groep (10) staat op twee tafels; de kleintjes schuiven aan tot en met tafel 103.
  await expect(page.getByLabel('Tafel 101')).toBeVisible();
  await expect(page.getByLabel('Tafel 102')).toBeVisible();
  await expect(page.getByLabel('Tafel 103')).toBeVisible();
  for (const name of ['De Kruumels', 'Fam. Bos', 'Jan Jansen']) {
    await expect(page.getByRole('cell', { name, exact: false }).first()).toBeVisible();
  }
  await expectNoSeriousA11yIssues(page);
});

test('een rij loopt eerst vol voordat de volgende begint', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await api.install(page);
  await page.goto('/beheer/verkoop/pronkzitting/tafelindeling');

  // Met 2 tafels per rij is rij 1 vol bij 16 plaatsen; De Kruumels (10) en Fam. Bos (6) vullen rij 1, Jan Jansen schuift door.
  await page.getByLabel('Tafels per rij').fill('2');
  await page.getByLabel('Tafels per rij').blur();
  await expect(page.getByText('Aantal tafels per rij opgeslagen.')).toBeVisible();

  await expect(page.getByLabel('Rij voor De Kruumels')).toHaveValue('0');
  await expect(page.getByLabel('Rij voor Fam. Bos')).toHaveValue('0');
  await expect(page.getByLabel('Rij voor Jan Jansen')).toHaveValue('1');
});

test('een gezelschap verplaatsen, automatisch verdelen en de afdruklijst downloaden', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await api.install(page);
  await page.goto('/beheer/verkoop/pronkzitting/tafelindeling');

  // Jan Jansen naar rij 4 verplaatsen.
  await page.getByLabel('Rij voor Jan Jansen').selectOption('3');
  await expect(page.getByLabel('Rij voor Jan Jansen')).toHaveValue('3');
  expect(api.seatingState['sp-vr']?.assignments['O:ord-12']?.row).toBe(3);

  // Automatisch verdelen wist de handmatige plaatsen.
  await page.getByRole('button', { name: 'Automatisch verdelen' }).click();
  await expect(page.getByText('De indeling is automatisch verdeeld.')).toBeVisible();
  expect(Object.keys(api.seatingState['sp-vr']?.assignments ?? {})).toHaveLength(0);

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Afdruklijst (Excel)' }).click();
  expect((await download).suggestedFilename()).toBe('Pronkzitting afdruklijst 5-2-2027.xlsx');
});
