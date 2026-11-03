import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { MockApi } from './mock-api';

/** Beheer → App-teksten: de teksten van de app aanpassen zonder nieuwe app in de store. */

test('app-teksten: zoeken, aanpassen en terugzetten', async ({ page }) => {
  const api = new MockApi(['config.manage']);
  await api.install(page);
  await page.goto('/beheer/app-teksten');

  await expect(page.getByRole('heading', { name: 'App-teksten', level: 1 })).toBeVisible();
  await expect(page.getByText('4 teksten, 1 aangepast')).toBeVisible();

  // Per scherm gegroepeerd; de teksten komen pas in de pagina als je de groep opent.
  const vereniging = page.getByRole('region', { name: 'Meer → Vereniging' });
  await expect(vereniging.getByRole('button', { name: '2 teksten tonen' })).toBeVisible();
  await expect(page.getByLabel('Bestuur')).toBeHidden();
  await vereniging.getByRole('button', { name: '2 teksten tonen' }).click();
  await expect(vereniging.getByLabel('Bestuur')).toBeVisible();

  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);

  // Aanpassen.
  await vereniging.getByLabel('Bestuur').fill('Het bestuur');
  await vereniging.getByRole('button', { name: 'Opslaan' }).first().click();
  await expect(vereniging.getByText('Opgeslagen.')).toBeVisible();
  expect(api.appTexts.find((t) => t.default === 'Bestuur')?.value).toBe('Het bestuur');
  expect(api.audit.map((a) => a.action)).toContain('config.app-text.changed');
  await expect(page.getByText('4 teksten, 2 aangepast')).toBeVisible();

  // Alleen de aangepaste teksten, in één platte lijst.
  await page.getByLabel('Alleen aangepaste teksten').check();
  const found = page.getByRole('region', { name: 'Gevonden teksten' });
  await expect(found.getByLabel(/Lid worden/)).toBeVisible();
  await expect(found.getByLabel('Opslaan')).toBeHidden();
  await page.getByLabel('Alleen aangepaste teksten').uncheck();

  // Zoeken vindt ook een tekst uit een onderdeel dat op meer schermen terugkomt.
  await page.getByLabel('Tekst zoeken').fill('opslaan');
  await expect(page.getByText('4 teksten, 2 aangepast · 1 gevonden')).toBeVisible();
  await expect(found.getByLabel('Onderdelen die op meer schermen terugkomen · Opslaan')).toBeVisible();
  await page.getByLabel('Tekst zoeken').fill('');

  // Terugzetten op de tekst uit de app.
  await vereniging.getByRole('button', { name: 'Terugzetten' }).click();
  await expect(vereniging.getByText('Teruggezet op de tekst uit de app.')).toBeVisible();
  expect(api.appTexts.find((t) => t.default === 'Bestuur')?.value).toBeNull();
  await expect(page.getByText('4 teksten, 1 aangepast')).toBeVisible();
});

test('app-teksten: geen horizontale overflow op een telefoon', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const api = new MockApi(['config.manage']);
  await api.install(page);
  await page.goto('/beheer/app-teksten');

  await expect(page.getByRole('heading', { name: 'App-teksten', level: 1 })).toBeVisible();
  await page
    .getByRole('region', { name: 'Meer → Vereniging' })
    .getByRole('button', { name: '2 teksten tonen' })
    .click();
  await expect(page.getByLabel('Bestuur')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
