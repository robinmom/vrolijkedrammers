import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 19t: tafelindeling pronkzitting — plattegrond (4 rijen naast elkaar × tafels van 8), starttafel kiezen, vullen en afdruklijst. */

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

test('een groep aan een gekozen starttafel zetten, ook als eerdere tafels leeg zijn', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await api.install(page);
  await page.goto('/beheer/verkoop/pronkzitting/tafelindeling');

  // Meer tafels per rij zodat tafel 205 bestaat.
  await page.getByLabel('Tafels per rij').fill('6');
  await page.getByLabel('Tafels per rij').blur();

  // De Kruumels (10) laten starten op tafel 205 — ook al zijn 201 t/m 204 leeg.
  await page.getByLabel('Starttafel voor De Kruumels').selectOption('205');
  await expect(page.getByLabel('Starttafel voor De Kruumels')).toHaveValue('205');
  expect(api.seatingState['sp-vr']?.assignments['G:De Kruumels']).toEqual({ row: 1, startSeat: 32 });

  // De groep bezet vanaf tafel 205 (10 plekken → 205 en 206).
  const row = api.buildSeating('sp-vr') as { parties: { name: string; tables: number[] }[] };
  expect(row.parties.find((p) => p.name === 'De Kruumels')?.tables).toEqual([205, 206]);
});

test('automatisch vult de vrije plekken, ook vóór een handmatig geplaatste groep', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await api.install(page);
  await page.goto('/beheer/verkoop/pronkzitting/tafelindeling');

  // De Kruumels handmatig op tafel 103; Fam. Bos en Jan Jansen blijven automatisch en vullen rij 1 vanaf tafel 101.
  await page.getByLabel('Starttafel voor De Kruumels').selectOption('103');
  await expect(page.getByLabel('Starttafel voor De Kruumels')).toHaveValue('103');
  const layout = api.buildSeating('sp-vr') as { parties: { name: string; manual: boolean; tables: number[] }[] };
  expect(layout.parties.find((p) => p.name === 'Fam. Bos')?.tables).toEqual([101]);
  expect(layout.parties.find((p) => p.name === 'Fam. Bos')?.manual).toBe(false);

  // Terug naar automatisch.
  await page.getByLabel('Starttafel voor De Kruumels').selectOption('auto');
  await expect(page.getByLabel('Starttafel voor De Kruumels')).toHaveValue('auto');
  expect(api.seatingState['sp-vr']?.assignments['G:De Kruumels']).toBeUndefined();
});

test('automatisch verdelen en de afdruklijst downloaden', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await api.install(page);
  await page.goto('/beheer/verkoop/pronkzitting/tafelindeling');

  await page.getByLabel('Starttafel voor Jan Jansen').selectOption('301');
  await page.getByRole('button', { name: 'Automatisch verdelen' }).click();
  await expect(page.getByText('De indeling is automatisch verdeeld.')).toBeVisible();
  expect(Object.keys(api.seatingState['sp-vr']?.assignments ?? {})).toHaveLength(0);

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Afdruklijst (Excel)' }).click();
  expect((await download).suggestedFilename()).toBe('Pronkzitting afdruklijst 5-2-2027.xlsx');
});
