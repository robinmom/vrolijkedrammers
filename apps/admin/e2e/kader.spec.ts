import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/**
 * Het kader (2026-11-17, eigen categorieën 2026-11-18): wie zit erin en hoe lang al, met een balk waarin elke drie
 * jaar (één periode) een streep staat. Bij een categorie met periodes kan een periode met één of twee jaar verlengd
 * worden en lopen de periodes door; de raad van elf heeft geen periode.
 */

const PERMISSIONS = ['member.read', 'member.update'];

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);
}

test('het kader met de balk per persoon en het einde van de lopende periode', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/kader');

  await expect(page.getByRole('heading', { name: 'Kader', level: 1 })).toBeVisible();
  await expect(page.getByText(/elke streep 3 jaar/)).toBeVisible();

  // De voorzitter zit er sinds 2019 in: dat is zijn derde periode.
  const voorzitter = page.getByRole('row', { name: /Test Bestuurder/ });
  await expect(voorzitter).toContainText('Voorzitter');
  await expect(voorzitter).toContainText('periode 3');
  await expect(voorzitter.getByRole('img', { name: /jaar bij bestuur/ })).toBeVisible();

  // De raad van elf heeft geen periode die afloopt.
  await page.getByRole('button', { name: 'Raad van Elf' }).click();
  await expect(page.getByRole('row', { name: /Raadslid Riet/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /verlengen voor/ })).toHaveCount(0);
  await expectNoSeriousA11yIssues(page);
});

test('een periode met twee jaar verlengen', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/kader');

  // De knoppen staan alleen bij een periode die (bijna) afloopt; de voorzitter zit nog middenin de zijne.
  await expect(
    page.getByRole('row', { name: /Test Bestuurder/ }).getByRole('button', { name: /\+2 jaar/ }),
  ).toHaveCount(0);
  await page
    .getByRole('row', { name: /Nieuwe Penningmeester/ })
    .getByRole('button', { name: /\+2 jaar/ })
    .click();

  await expect(page.getByRole('status')).toContainText('met 2 jaar verlengd');
  await expect(page.getByRole('row', { name: /Nieuwe Penningmeester/ })).toContainText('verlengd met 2 jaar');
  expect(api.boardSeats.find((s) => s.id === 'b-2')!.terms.at(-1)!.extensionYears).toBe(2);
});

test('het einde van de periode loopt mee met de jaren', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  // Iemand die al tien jaar in het bestuur zit, met alleen zijn eerste periode vastgelegd.
  const start = new Date();
  start.setUTCFullYear(start.getUTCFullYear() - 10);
  const startDate = start.toISOString().slice(0, 10);
  api.boardSeats.push({
    id: 'b-9',
    bodyId: 'bb-1',
    memberId: null,
    name: 'Jan Jubileum',
    function: null,
    startDate,
    endDate: null,
    notes: null,
    terms: [{ startDate, extensionYears: 0, note: null }],
  });
  await api.install(page);
  await page.goto('/beheer/kader');

  // Niet "periode 1" met een einde van tien jaar terug, maar de vierde periode die nu loopt.
  const row = page.getByRole('row', { name: /Jan Jubileum/ });
  await expect(row).toContainText('periode 4');
  await expect(row).toContainText(String(start.getUTCFullYear() + 12));
});

test('een eigen categorie toevoegen en gebruiken', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/kader/categorieen');

  await expect(page.getByRole('heading', { name: 'Categorieën in het kader', level: 1 })).toBeVisible();
  await expect(page.getByRole('row', { name: /Bestuur/ })).toContainText('elke drie jaar');

  await page.getByRole('button', { name: 'Categorie toevoegen' }).click();
  await page.getByRole('dialog').getByLabel('Naam').fill('Jeugdraad');
  await page.getByRole('dialog').getByLabel('Volgorde').fill('3');
  await page.getByRole('button', { name: 'Opslaan' }).click();

  await expect(page.getByRole('row', { name: /Jeugdraad/ })).toContainText('geen');
  expect(api.boardBodies.at(-1)).toMatchObject({ name: 'Jeugdraad', hasTerms: false, position: 3 });
  await expectNoSeriousA11yIssues(page);

  // De nieuwe categorie staat meteen als knop bij het overzicht, zonder periodes.
  await page.goto('/beheer/kader');
  await page.getByRole('button', { name: 'Jeugdraad' }).click();
  await page.getByRole('button', { name: 'Toevoegen' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel('Categorie')).toHaveValue(api.boardBodies.at(-1)!.id);
  await expect(page.getByRole('group', { name: 'Periodes' })).toHaveCount(0);
  await page.getByLabel('Of een naam zelf').fill('Kim Jeugdprinses');
  await page.getByLabel('In functie sinds').fill('2026-01-10');
  await page.getByRole('button', { name: 'Opslaan' }).click();

  expect(api.boardSeats.at(-1)).toMatchObject({
    bodyId: api.boardBodies.at(-1)!.id,
    name: 'Kim Jeugdprinses',
    terms: [],
  });
});

test('een categorie met mensen erin gaat niet weg', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/kader/categorieen');

  // Bestuur heeft twee mensen: de knop staat uit.
  await expect(page.getByRole('row', { name: /Bestuur/ })).toContainText('2');
  await expect(page.getByRole('row', { name: /^Bestuur/ }).getByRole('button', { name: /Verwijderen/ })).toBeDisabled();
  expect(api.boardBodies).toHaveLength(2);
});

test('een nieuw raadslid toevoegen zonder periodes', async ({ page }) => {
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/kader');

  await page.getByRole('button', { name: 'Toevoegen' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Categorie').selectOption('bb-2');
  await expect(page.getByRole('group', { name: 'Periodes' })).toHaveCount(0);
  await page.getByLabel('Of een naam zelf').fill('Nieuw Raadslid');
  await page.getByLabel('In functie sinds').fill('2026-11-11');
  await page.getByRole('button', { name: 'Opslaan' }).click();

  expect(api.boardSeats.at(-1)).toMatchObject({ bodyId: 'bb-2', name: 'Nieuw Raadslid', terms: [] });
});

test('op een smal scherm loopt niets buiten beeld', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const api = new MockApi(PERMISSIONS);
  await api.install(page);
  await page.goto('/beheer/kader');

  await expect(page.getByRole('heading', { name: 'Kader', level: 1 })).toBeVisible();
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(1);
});
