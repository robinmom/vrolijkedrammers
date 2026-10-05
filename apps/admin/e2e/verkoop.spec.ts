import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 19: Verkoop in het portal — een pagina per soort product, pronkzitting per avond, wachtlijst en munten. */

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

test('fase 19: menukop Verkoop met een pagina per product', async ({ page }) => {
  const api = new MockApi(['sale.manage', 'member.read']);
  await open(page, api, 'verkoop/pronkzitting');
  await expect(page.getByRole('heading', { name: 'Pronkzitting', level: 1 })).toBeVisible();
  const toggle = page.getByRole('button', { name: 'Menu' });
  if (await toggle.isVisible()) await toggle.click();
  const nav = page.getByRole('navigation');
  await expect(nav.getByText('Verkoop', { exact: true })).toBeVisible();
  await expect(nav.getByText('Kaartverkoop')).toHaveCount(0);
  for (const name of ['Pronkzitting', 'Dagkaarten', 'Activiteiten', 'Munten', 'Kassalog']) {
    await expect(nav.getByRole('link', { name })).toBeVisible();
  }
  await expect(nav.getByRole('link', { name: 'Pronkzitting' })).toHaveAttribute('aria-current', 'page');
});

test('fase 19: pronkzitting per avond, export, wachtlijst en de bestellingen', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await open(page, api, 'verkoop/pronkzitting');
  await expect(page.getByRole('row', { name: /De Kruumels groep 4 Mendy Mom/ })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Bij De Kruumels zitten' })).toBeVisible();
  // Alleen de avonden, niet de munten of dagkaarten.
  const avonden = page.getByRole('region', { name: 'Avonden' });
  await expect(avonden.getByRole('row', { name: /Pronkzitting vrijdag/ })).toBeVisible();
  await expect(avonden.getByText('Dagkaart zaterdag')).toHaveCount(0);
  await expect(
    page.getByRole('region', { name: 'Bestellingen' }).getByRole('row', { name: /2027-0142/ }),
  ).toBeVisible();
  await expect(page.getByRole('region', { name: 'Bestellingen' }).getByRole('row', { name: /2027-0101/ })).toHaveCount(
    0,
  );
  await expectNoSeriousA11yIssues(page);

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exporteren voor tafelindeling (Excel)' }).click();
  expect((await download).suggestedFilename()).toBe('Pronkzitting tafelindeling 29-9-2026.xlsx');

  await expect(page.getByRole('button', { name: 'Toekennen De Snotapen' })).toBeDisabled();
  await page.getByRole('button', { name: 'Toekennen Jan Jansen' }).click();
  const form = page.getByRole('form', { name: 'Toekennen: Jan Jansen' });
  await expect(form.getByLabel('Uitnodigen met een betaallink (48 uur geldig)')).toBeChecked();
  await form.getByRole('button', { name: 'Toekennen' }).click();
  await expect(page.getByText('Jan Jansen heeft de plaatsen gekregen en krijgt een e-mail.')).toBeVisible();
  expect(api.salesCalls.at(-1)).toMatchObject({
    path: '/admin/sales/waitlist/w-2/grant',
    body: { payment: 'PaymentLink' },
  });

  // Contant ontvangen voor een openstaande betaallink.
  await page.getByRole('button', { name: 'Contant ontvangen 2027-0142' }).click();
  await expect(page.getByText('2027-0142 contant betaald; bevestiging gemaild.')).toBeVisible();
});

test('fase 19: nieuwe pronkzittingbestelling met groepskaarten, contant', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await open(page, api, 'verkoop/pronkzitting');
  await page.getByRole('button', { name: '+ Bestelling' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nieuwe bestelling' });
  await expect(dialog.getByLabel('Product').locator('option', { hasText: 'Dagkaart' })).toHaveCount(0);
  await dialog.getByLabel('Product').selectOption('sp-za');
  await dialog.getByLabel('Groep').selectOption('De Kruumels');
  await expect(dialog.getByLabel('Groep').locator('option:checked')).toHaveText('De Kruumels (nog 5 van 13 personen)');
  await dialog.getByLabel('Aantal groepskaarten').fill('5');
  await dialog.getByLabel('Losse kaarten (niet-leden)').fill('2');
  await expect(dialog.getByText('totaal € 25,00')).toBeVisible();
  await dialog.getByLabel('Naam besteller').fill('Mendy Mom');
  await dialog.getByLabel('E-mailadres').fill('mendy@example.com');
  await dialog.getByLabel('Contant ontvangen').check();
  await expectNoSeriousA11yIssues(page);
  await dialog.getByRole('button', { name: 'Bestelling opslaan' }).click();
  await expect(page.getByText('Bestelling 2027-0200 aangemaakt en bevestigd; de kaarten zijn gemaild.')).toBeVisible();
  expect(api.salesCalls.at(-1)).toMatchObject({
    path: '/admin/sales/orders',
    body: { productId: 'sp-za', groupName: 'De Kruumels', memberQuantity: 5, paidQuantity: 2, payment: 'Cash' },
  });
});

test('fase 19: dagkaarten, eigen pagina met betaallink voor de vrije verkoop', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await open(page, api, 'verkoop/dagkaarten');
  await expect(page.getByRole('heading', { name: 'Dagkaarten', level: 1 })).toBeVisible();
  await expect(page.getByRole('row', { name: /Dagkaart zaterdag/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Pronkzitting vrijdag/ })).toHaveCount(0);
  await expectNoSeriousA11yIssues(page);

  await page.getByRole('button', { name: 'Betaallink maken' }).click();
  const link = page.getByRole('dialog', { name: 'Betaallink maken' });
  await link.getByLabel('Product').selectOption('sp-dag');
  await link.getByLabel('Aantal').fill('3');
  await link.getByLabel('Naam besteller').fill('Kees');
  await link.getByLabel('E-mailadres').fill('kees@example.com');
  await link.getByRole('button', { name: 'Betaallink versturen' }).click();
  await expect(page.getByText('de betaallink is gemaild naar kees@example.com')).toBeVisible();
  expect(api.salesCalls.at(-1)).toMatchObject({
    body: { productId: 'sp-dag', paidQuantity: 3, payment: 'PaymentLink' },
  });

  // Een nieuw product op deze pagina is altijd een dagkaart.
  await page.getByRole('button', { name: '+ Product' }).click();
  await expect(page.getByRole('dialog', { name: 'Nieuw product' }).getByLabel('Soort')).toHaveCount(0);
});

test('fase 19: activiteiten en munten hebben een eigen pagina', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await open(page, api, 'verkoop/activiteiten');
  await expect(page.getByRole('heading', { name: 'Activiteiten', level: 1 })).toBeVisible();
  await expect(page.getByText('Nog geen kaarten voor activiteiten.', { exact: false })).toBeVisible();

  await page.goto('/beheer/verkoop/munten');
  await expect(page.getByRole('heading', { name: 'Munten', level: 1 })).toBeVisible();
  await expect(page.getByRole('row', { name: /Mendy Mom .* 20 .* Betaald · af te halen/ })).toBeVisible();
  await expect(page.getByText('Af te halen', { exact: true })).toBeVisible();
  await expectNoSeriousA11yIssues(page);
});

test('munten: het bestuur verplaatst ze één keer naar een ander toestel van het lid', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await open(page, api, 'verkoop/munten');
  const toestel = page.getByRole('button', { name: /^Toestel/ }).first();
  await toestel.click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('iPhone 15')).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Verplaatsen' })).toBeDisabled();
  await dialog.getByLabel('Nieuw toestel').selectOption('dev-2');
  await dialog.getByLabel('Reden').fill('Telefoon kapot');
  await expectNoSeriousA11yIssues(page);
  await dialog.getByRole('button', { name: 'Verplaatsen' }).click();
  await expect(page.getByText(/verplaatst\. Dit kan niet nog een keer\./)).toBeVisible();

  await toestel.click();
  await expect(page.getByRole('dialog').getByText(/Nog een keer verplaatsen kan niet/)).toBeVisible();
  await expect(page.getByRole('dialog').getByRole('button', { name: 'Verplaatsen' })).toHaveCount(0);
});

test('leden: bij een lid staat de groep uit e-Boekhouden', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'leden/m-1');
  const groep = page.locator('dt', { hasText: /^Groep$/ });
  await expect(groep.locator('xpath=following-sibling::dd[1]')).toHaveText('De Kruumels');
});

test('fase 19c: kassalog met uitgiftes en geweigerde scans', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await open(page, api, 'verkoop/kassalog');
  await expect(page.getByRole('heading', { name: 'Kassalog', level: 1 })).toBeVisible();
  await expect(
    page.getByRole('row', { name: /Mendy Mom Kruumels 20 2027-0311 .* Uitgegeven Kassa 1 · Kees/ }),
  ).toBeVisible();
  await expect(page.getByText('Geweigerd: ander toestel')).toBeVisible();
  await expect(page.getByText('1.240')).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Kerncijfers kassa' }).getByText('1240')).toBeVisible();
  await expectNoSeriousA11yIssues(page);
  await page.getByLabel('Dag').fill('2027-02-12');
  await expect(page.getByRole('heading', { name: 'Scans op 12 feb 2027' })).toBeVisible();
});
