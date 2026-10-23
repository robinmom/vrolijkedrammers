import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 4d: verzamelfacturen per onderdeel en carnavalsjaar, met de delta (alleen nieuw-betaalde regels). */

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

test('verzamelfactuur maken van de betaalde adverteerders; delta daarna leeg', async ({ page }) => {
  const api = new MockApi(['finance.read']);
  await open(page, api, 'financieel/verzamelfacturen');

  await expect(page.getByRole('heading', { name: /Verzamelfacturen/, level: 1 })).toBeVisible();

  // De preview toont de betaalde transacties die nog op geen factuur staan.
  const preview = page.getByRole('region', { name: 'Nieuwe transacties' });
  await expect(preview.getByText('Movement-IT B.V.')).toBeVisible();
  await expect(preview.getByText('Smashstudios')).toBeVisible();

  await page.getByRole('button', { name: 'Verzamelfactuur maken' }).click();

  // De gemaakte factuur verschijnt in de lijst…
  const made = page.getByRole('region', { name: 'Verzamelfacturen' });
  await expect(made.getByText('VF-2027-ADVE-01')).toBeVisible();
  // …en er is niets nieuws meer om te factureren (delta leeg).
  await expect(page.getByText(/Er zijn geen nieuwe betaalde transacties/)).toBeVisible();

  await expectNoSeriousA11yIssues(page);
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(1);
});
