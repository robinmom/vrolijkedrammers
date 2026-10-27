import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Agenda-categorieën zelf beheren: toevoegen, hernoemen, verwijderen. */

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

test('agenda-categorieën beheren: toevoegen en de teller tonen', async ({ page }) => {
  const api = new MockApi(['event.manage']);
  await open(page, api, 'agenda');

  await page.getByRole('button', { name: 'Categorieën' }).click();
  const dialog = page.getByRole('dialog', { name: 'Agenda-categorieën' });
  await expect(dialog.getByLabel('Naam categorie Carnaval')).toBeVisible();
  await expect(dialog.getByText('1 activiteit')).toBeVisible();

  // Carnaval is in gebruik (1 activiteit) → geen verwijderknop; Jeugd wel.
  await expect(dialog.getByRole('button', { name: 'Verwijderen' })).toHaveCount(1);

  await dialog.getByLabel('Nieuwe categorie').fill('Pronkzitting');
  await dialog.getByRole('button', { name: 'Toevoegen' }).click();
  await expect(dialog.getByLabel('Naam categorie Pronkzitting')).toBeVisible();

  await expectNoSeriousA11yIssues(page);
});

test('een net aangemaakte categorie is meteen toe te wijzen aan een nieuw item', async ({ page }) => {
  const api = new MockApi(['event.manage']);
  await open(page, api, 'agenda');

  // Nieuwe categorie aanmaken en de dialoog sluiten.
  await page.getByRole('button', { name: 'Categorieën' }).click();
  const dialog = page.getByRole('dialog', { name: 'Agenda-categorieën' });
  await dialog.getByLabel('Nieuwe categorie').fill('Pronkzitting');
  await dialog.getByRole('button', { name: 'Toevoegen' }).click();
  await expect(dialog.getByLabel('Naam categorie Pronkzitting')).toBeVisible();
  await dialog.getByRole('button', { name: 'Sluiten' }).click();

  // In een nieuw item staat de nieuwe categorie meteen in de keuzelijst (beheer-lijst, niet de gecachete publieke lijst).
  await page.getByRole('link', { name: 'Event toevoegen' }).click();
  await expect(page.getByRole('heading', { name: 'Event toevoegen' })).toBeVisible();
  const categorie = page.getByLabel('Categorie');
  await expect(categorie.getByRole('option', { name: 'Pronkzitting' })).toHaveCount(1);
  await categorie.selectOption({ label: 'Pronkzitting' });
  await expect(categorie.locator('option:checked')).toHaveText('Pronkzitting');
});
