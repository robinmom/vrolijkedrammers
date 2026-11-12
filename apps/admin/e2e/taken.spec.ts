import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/**
 * Takenlijst carnaval. Sinds 2026-11-12 houdt degene die de taak doet er in de app een opmerking bij; het bestuur
 * leest en bewerkt die hier.
 */

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(serious.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)).toEqual([]);
}

test('het bestuur leest en bewerkt de opmerking van de uitvoerder', async ({ page }) => {
  const api = new MockApi(['task.manage']);
  await api.install(page);
  await page.goto('/beheer/taken');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Takenlijst carnaval');

  await page.getByRole('button', { name: 'Zaal reserveren' }).first().click();
  const dialog = page.getByRole('dialog');
  const note = dialog.getByLabel('Opmerking van de uitvoerder');
  await expect(note).toHaveValue('Ereleden zijn verstuurd, de sponsoren volgen.');
  await note.fill('Sponsoren zijn ook geweest.');
  await dialog.getByRole('button', { name: 'Opslaan' }).click();
  await expect(dialog).toBeHidden();

  await expectNoSeriousA11yIssues(page);
});

test('takenlijst: geen horizontale overflow op een telefoon', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const api = new MockApi(['task.manage']);
  await api.install(page);
  await page.goto('/beheer/taken');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  // Ook met het dialoog open moet het passen; daar staat het nieuwe veld in.
  await page.getByRole('button', { name: 'Zaal reserveren' }).first().click();
  await expect(page.getByRole('dialog')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
