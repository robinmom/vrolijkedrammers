import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 22c: uitslag voor de uitslagcommissie — per categorie, Excel en publiceren na de prijsuitreiking. */

async function open(page: Page, api: MockApi, path = '') {
  await api.install(page);
  await page.goto(`/beheer/${path}`);
}

test('fase 22c: uitslag per categorie, Excel-exports en publiceren pas na bevestiging', async ({ page }) => {
  const api = new MockApi(['parade.result']);
  await open(page, api, '');
  await expect(page).toHaveURL(/\/beheer\/optocht\/uitslag$/);
  await expect(page.getByText(/nog niet gepubliceerd/)).toBeVisible();
  await expect(page.getByRole('row', { name: /1 64 De Droatneagels .*420 432 912 440 2\.204/ })).toBeVisible();

  await page.getByRole('tab', { name: /Wagens jeugd/ }).click();
  await expect(page.getByText('3 van 5 juryleden hebben ingediend.', { exact: false })).toBeVisible();
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Excel: zaallijst' }).click();
  expect((await download).suggestedFilename()).toBe('zaallijst-20270207.xlsx');

  // Nog niet alle categorieën klaar: publiceren kan niet.
  await expect(page.getByRole('button', { name: 'Nu publiceren' })).toBeDisabled();
  api.results.categories[1]!.ready = true;
  api.results.categories[1]!.submitted = 5;
  await page.reload();
  await page.getByRole('button', { name: 'Nu publiceren' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Is de prijsuitreiking al geweest?' })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Uitslag publiceren' })).toBeDisabled();
  await dialog.getByLabel('Ja, de prijsuitreiking is geweest').check();
  await dialog.getByRole('button', { name: 'Uitslag publiceren' }).click();
  await expect(page.getByText('De uitslag is gepubliceerd: hij staat nu op de website en in de app.')).toBeVisible();
  await expect(page.getByText(/gepubliceerd op/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Nu publiceren' })).toHaveCount(0);
  expect(api.audit.map((a) => a.action)).toContain('parade.results-published');
});
