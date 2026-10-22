import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 4c: Financieel — totaaloverzicht en statistieken per carnavalsjaar, en de facturen met tabbladen. */

async function open(page: Page, api: MockApi, path = '') {
  await api.install(page);
  await page.goto(`/beheer/${path}`);
}

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(
    serious.map((v) => `${v.id}: ${v.nodes.map((n) => `${n.target.join(' ')} ${n.failureSummary}`).join(', ')}`),
  ).toEqual([]);
}

async function expectNoOverflow(page: Page) {
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(1);
}

test('totaaloverzicht: inkomsten per bron van het carnavalsjaar', async ({ page }) => {
  const api = new MockApi(['finance.read']);
  await open(page, api, 'financieel');

  await expect(page.getByRole('heading', { name: /Totaaloverzicht/, level: 1 })).toBeVisible();
  const table = page.getByRole('region', { name: 'Inkomsten per bron' });
  await expect(table.getByText('Adverteerders')).toBeVisible();
  await expect(table.getByText('Dansgardefestival')).toBeVisible();
  await expect(table.getByText('Kaartverkoop')).toBeVisible();

  await expectNoSeriousA11yIssues(page);
  await expectNoOverflow(page);
});

test('statistieken: alles incl. contributie, met grafieken per bron en per jaar', async ({ page }) => {
  const api = new MockApi(['finance.read']);
  await open(page, api, 'financieel/statistieken');

  await expect(page.getByRole('heading', { name: /Statistieken/, level: 1 })).toBeVisible();
  await expect(page.getByRole('heading', { name: /Inkomsten per bron/ })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Totaal per carnavalsjaar' })).toBeVisible();
  const table = page.getByRole('region', { name: 'Verdeling per bron' });
  await expect(table.getByText('Contributie')).toBeVisible();

  await expectNoSeriousA11yIssues(page);
  await expectNoOverflow(page);
});

test('facturen: tabbladen voor adverteerders en Dansgardefestival', async ({ page }) => {
  const api = new MockApi(['finance.read', 'advertiser.manage']);
  await open(page, api, 'financieel/facturen');

  // Standaard het tabblad Adverteerders.
  await expect(page.getByRole('heading', { name: /Facturen/, level: 1 })).toBeVisible();

  // Wissel naar het festivaltabblad.
  await page.getByRole('tab', { name: 'Dansgardefestival' }).click();
  const table = page.getByRole('region', { name: 'Festivalfacturen' });
  await expect(table.getByText('FEST-2027-0001')).toBeVisible();
  await expect(table.getByText('DV Loil')).toBeVisible();

  await expectNoSeriousA11yIssues(page);
  await expectNoOverflow(page);
});
