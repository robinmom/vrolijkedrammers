import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 22a: jury van de optocht — uitnodigen zonder categorie, per optocht indelen, hoofdjury en weging. */

async function open(page: Page, api: MockApi, path = '') {
  await api.install(page);
  await page.goto(`/beheer/${path}`);
}

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(serious.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)).toEqual([]);
}

test('fase 22a: bestuur nodigt een jurylid uit, deelt hem in en maakt hem hoofdjury; weging per categorie', async ({
  page,
}) => {
  const api = new MockApi(['jury.manage', 'jury.assign', 'parade.config']);
  await open(page, api, 'optocht/jury');
  await expect(page.getByRole('heading', { name: 'Jury', level: 1 })).toBeVisible();
  await expect(page.getByText('Optocht Loil 2027')).toBeVisible();
  await expect(page.getByRole('row', { name: /Anke Jansen .*Hoofdjury.*Getrokken wagens volwassenen/ })).toBeVisible();

  await page.getByRole('button', { name: 'Jurylid uitnodigen' }).click();
  let dialog = page.getByRole('dialog');
  await dialog.getByLabel('Naam').fill('Carla Smit');
  await dialog.getByLabel('E-mailadres').fill('carla@example.com');
  await dialog.getByRole('button', { name: 'Uitnodiging versturen' }).click();
  await expect(page.getByText('Carla Smit is uitgenodigd.')).toBeVisible();
  await expect(page.getByRole('row', { name: /Carla Smit .*Nog geen categorie.*Uitgenodigd/ })).toBeVisible();
  expect(api.audit.map((a) => a.action)).toContain('jury.invited');

  await page.getByRole('button', { name: 'Aanpassen Carla Smit' }).click();
  dialog = page.getByRole('dialog');
  await dialog.getByLabel('Loopgroepen groot volwassenen').check();
  await dialog.getByLabel(/Hoofdjury/).check();
  await expect(dialog.getByRole('button', { name: 'Uitnodiging opnieuw sturen' })).toBeVisible();
  await expectNoSeriousA11yIssues(page);
  await dialog.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByRole('row', { name: /Carla Smit .*Hoofdjury.*Loopgroepen groot volwassenen/ })).toBeVisible();
  expect(api.jury.jurors.find((j) => j.name === 'Carla Smit')).toMatchObject({ headJury: true, categoryIds: [3] });

  await page.getByRole('button', { name: 'Weging Getrokken wagens volwassenen' }).click();
  dialog = page.getByRole('dialog');
  await dialog.getByLabel('Kwaliteit').selectOption('3');
  await dialog.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Weging van Getrokken wagens volwassenen is opgeslagen.')).toBeVisible();
  expect(api.jury.categories[0]).toMatchObject({ quality: 3, judged: true });
  await expectNoSeriousA11yIssues(page);
});

test('fase 22a: de hoofdjury ziet alleen Jury en mag alleen indelen', async ({ page }) => {
  const api = new MockApi(['jury.assign']);
  await open(page, api, '');
  await expect(page).toHaveURL(/\/beheer\/optocht\/jury$/);
  await expect(page.getByRole('heading', { name: 'Jury', level: 1 })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Jurylid uitnodigen' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Weging/ })).toHaveCount(0);

  await page.getByRole('button', { name: 'Aanpassen Bert de Vries' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel(/Hoofdjury/)).toHaveCount(0);
  await expect(dialog.getByRole('button', { name: 'Uit de jury halen' })).toHaveCount(0);
  await dialog.getByLabel('Loopgroepen groot volwassenen').uncheck();
  await dialog.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByRole('row', { name: /Bert de Vries/ })).not.toContainText('Loopgroepen groot volwassenen');
  expect(api.jury.jurors.find((j) => j.name === 'Bert de Vries')!.categoryIds).toEqual([1]);
});

test('fase 22a: bestuur haalt een jurylid uit de jury', async ({ page }) => {
  const api = new MockApi(['jury.manage', 'jury.assign']);
  await open(page, api, 'optocht/jury');
  await page.getByRole('button', { name: 'Aanpassen Bert de Vries' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Uit de jury halen' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Uit de jury halen' }).click();
  await expect(page.getByText('Bert de Vries is uit de jury gehaald.')).toBeVisible();
  await expect(page.getByRole('row', { name: /Bert de Vries/ })).toHaveCount(0);
});

test('fase 22b: voortgang per jurylid en beoordelingen buiten categorie per inzending goedkeuren, zonder scores', async ({
  page,
}) => {
  const api = new MockApi(['jury.assign']);
  await open(page, api, 'optocht/jury');
  await expect(page.getByRole('row', { name: /Anke Jansen .*Ingediend/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Bert de Vries .*Bezig 9\/19/ })).toBeVisible();
  await expect(
    page.getByText(
      /Bert de Vries heeft 2 inzendingen gejureerd buiten de eigen categorieën \(Loopgroepen groot jeugd\)/,
    ),
  ).toBeVisible();

  await page.getByRole('button', { name: 'Aanpassen beoordelingen buiten categorie van Bert de Vries' }).click();
  const dialog = page.getByRole('dialog');
  await expect(
    dialog.getByRole('row', { name: /6 De jeugdige Sökkels Loopgroepen groot jeugd 3 passages/ }),
  ).toBeVisible();
  await expect(dialog.getByText(/scores zijn alleen voor het jurylid zelf/)).toBeVisible();
  await expectNoSeriousA11yIssues(page);
  await dialog.getByRole('button', { name: 'Akkoord De jeugdige Sökkels' }).click();
  await expect(dialog.getByRole('row', { name: /De jeugdige Sökkels/ })).toContainText('Akkoord');
  await dialog.getByRole('button', { name: 'Afwijzen DwarZ' }).click();
  await expect(dialog.getByRole('row', { name: /DwarZ/ })).toContainText('Afgewezen');
  await dialog.getByRole('button', { name: 'Klaar' }).click();
  await expect(page.getByText(/gejureerd buiten de eigen categorieën/)).toHaveCount(0);
  expect(api.jury.outside.map((o) => o.decision)).toEqual(['Approved', 'Rejected']);
});

test('fase 22b: alles van een jurylid in één keer goedkeuren', async ({ page }) => {
  const api = new MockApi(['jury.manage', 'jury.assign']);
  await open(page, api, 'optocht/jury');
  await page.getByRole('button', { name: 'Akkoord alles van Bert de Vries' }).click();
  await expect(page.getByText(/gejureerd buiten de eigen categorieën/)).toHaveCount(0);
  expect(api.jury.outside.every((o) => o.decision === 'Approved')).toBe(true);
});
