import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 19a: kaartverkoop in het portal — producten, bestellingen, pronkzitting per avond, wachtlijst en munten. */

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

test('fase 19: eigen menukop Kaartverkoop en het verkoopoverzicht', async ({ page }) => {
  const api = new MockApi(['sale.manage', 'member.read']);
  await open(page, api, 'kaartverkoop');
  await expect(page.getByRole('heading', { name: 'Kaartverkoop', level: 1 })).toBeVisible();
  const toggle = page.getByRole('button', { name: 'Menu' });
  if (await toggle.isVisible()) await toggle.click();
  const nav = page.getByRole('navigation');
  await expect(nav.getByText('Kaartverkoop', { exact: true }).first()).toBeVisible();
  await expect(nav.getByRole('link', { name: 'Pronkzitting' })).toBeVisible();
  await expect(nav.getByRole('link', { name: 'Munten' })).toBeVisible();
  await expect(nav.getByRole('link', { name: 'Kaartverkoop' })).toHaveAttribute('aria-current', 'page');
  if (await toggle.isVisible()) await toggle.click();

  await expect(page.getByText('€ 3.482,50')).toBeVisible();
  const vrijdag = page.getByRole('row', { name: /Pronkzitting vrijdag/ });
  await expect(vrijdag.getByText('Vol')).toBeVisible();
  await expect(vrijdag.getByText('2 wachtlijst')).toBeVisible();
  await expectNoSeriousA11yIssues(page);

  // Contant ontvangen voor een openstaande betaallink.
  await page.getByRole('button', { name: 'Contant ontvangen 2027-0142' }).click();
  await expect(page.getByText('2027-0142 contant betaald; kaarten gemaild.')).toBeVisible();
  expect(api.salesCalls.at(-1)).toMatchObject({ method: 'POST', path: '/admin/sales/orders/so-1/paid-cash' });
});

test('fase 19: nieuwe bestelling met groepskaarten en een losse betaallink', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await open(page, api, 'kaartverkoop');
  await page.getByRole('button', { name: '+ Bestelling' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nieuwe bestelling' });
  await dialog.getByLabel('Product').selectOption('sp-za');
  await dialog.getByLabel('Groep').selectOption('De Kruumels');
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

  await page.getByRole('button', { name: 'Betaallink maken' }).click();
  const link = page.getByRole('dialog', { name: 'Betaallink maken' });
  await link.getByLabel('Product').selectOption('sp-za');
  await expect(link.getByLabel('Groep')).toHaveCount(0);
  await link.getByLabel('Aantal').fill('3');
  await link.getByLabel('Naam besteller').fill('Kees');
  await link.getByLabel('E-mailadres').fill('kees@example.com');
  await link.getByRole('button', { name: 'Betaallink versturen' }).click();
  await expect(page.getByText('de betaallink is gemaild naar kees@example.com')).toBeVisible();
  expect(api.salesCalls.at(-1)).toMatchObject({ body: { paidQuantity: 3, memberQuantity: 0, payment: 'PaymentLink' } });
});

test('fase 19: pronkzitting per avond, export voor de tafelindeling en de wachtlijst', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await open(page, api, 'kaartverkoop/pronkzitting');
  await expect(page.getByRole('heading', { name: 'Pronkzitting', level: 1 })).toBeVisible();
  await expect(page.getByRole('row', { name: /De Kruumels groep 4 Mendy Mom/ })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Bij De Kruumels zitten' })).toBeVisible();
  await expectNoSeriousA11yIssues(page);

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exporteren voor tafelindeling (Excel)' }).click();
  expect((await download).suggestedFilename()).toBe('Pronkzitting tafelindeling 29-9-2026.xlsx');

  // De groep past niet meer; de losse kaarten wel: toekennen met een betaallink.
  await expect(page.getByRole('button', { name: 'Toekennen De Snotapen' })).toBeDisabled();
  await page.getByRole('button', { name: 'Toekennen Jan Jansen' }).click();
  const dialog = page.getByRole('dialog', { name: 'Toekennen: Jan Jansen' });
  await expect(dialog.getByLabel('Uitnodigen met een betaallink (48 uur geldig)')).toBeChecked();
  await dialog.getByRole('button', { name: 'Toekennen' }).click();
  await expect(page.getByText('Jan Jansen heeft de plaatsen gekregen en krijgt een e-mail.')).toBeVisible();
  expect(api.salesCalls.at(-1)).toMatchObject({
    path: '/admin/sales/waitlist/w-2/grant',
    body: { payment: 'PaymentLink' },
  });
});

test('fase 19: munten, alleen leden en af te halen', async ({ page }) => {
  const api = new MockApi(['sale.manage']);
  await open(page, api, 'kaartverkoop/munten');
  await expect(page.getByRole('heading', { name: 'Munten', level: 1 })).toBeVisible();
  await expect(page.getByRole('row', { name: /Mendy Mom .* 20 .* Betaald · af te halen/ })).toBeVisible();
  await expectNoSeriousA11yIssues(page);
});
