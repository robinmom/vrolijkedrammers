import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { MockApi } from './mock-api';

/**
 * Eerste jaar contributie bij een laat lidmaatschap (wens 2026-11-05): wie zich aanmeldt ná de contributie-inning en
 * vóór carnaval valt buiten die inning. De secretaris stuurt een betaalverzoek; de aanmelding blijft in behandeling
 * tot er betaald is en wordt dan vanzelf goedgekeurd.
 */

test('betaalverzoek sturen in plaats van goedkeuren, en de aanmelding blijft onbetaald in behandeling', async ({ page }) => {
  const api = new MockApi(['member.read', 'member.update', 'member.approve']);
  await api.install(page);
  await page.goto('/beheer/aanmeldingen/ap-2');

  await expect(page.getByRole('heading', { name: /Twan Derksen/, level: 1 })).toBeVisible();

  // Uitleg waaróm er betaald moet worden, met het eenpersoonstarief.
  const melding = page.getByRole('status').filter({ hasText: 'Eerste jaar contributie' });
  await expect(melding).toContainText('€ 32,50');
  await expect(melding).toContainText('na de contributie-inning');

  // Goedkeuren kan hier niet: de betaling keurt goed.
  await expect(page.getByRole('button', { name: 'Goedkeuren' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Betaalverzoek sturen' }).click();

  await expect(page.getByText('In behandeling (onbetaald)')).toBeVisible();
  await expect(melding).toContainText('nog niet betaald');
  await expect(melding).toContainText('Vervalt op');
  // Een herinnering sturen moet kunnen; een tweede betaling aanmaken niet.
  await expect(page.getByRole('button', { name: 'Betaallink opnieuw sturen' })).toBeVisible();

  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);
});

test('een gewone aanmelding houdt de knop Goedkeuren', async ({ page }) => {
  const api = new MockApi(['member.read', 'member.update', 'member.approve']);
  await api.install(page);
  await page.goto('/beheer/aanmeldingen/ap-1');

  await expect(page.getByRole('button', { name: 'Goedkeuren' })).toBeVisible();
  await expect(page.getByText('Eerste jaar contributie')).toHaveCount(0);
});

test('op een smal scherm loopt niets buiten beeld', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const api = new MockApi(['member.read', 'member.update', 'member.approve']);
  await api.install(page);
  await page.goto('/beheer/aanmeldingen/ap-2');

  await expect(page.getByRole('heading', { name: /Twan Derksen/, level: 1 })).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
});
