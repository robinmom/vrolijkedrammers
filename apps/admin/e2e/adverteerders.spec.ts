import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 27b: adverteerders inlezen, beheren en de campagne volgen. */

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);
}

test('fase 27b: overzicht, collectant koppelen en IBAN voluit', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  await api.install(page);
  await page.goto('/beheer/adverteerders');

  await expect(page.getByRole('heading', { name: 'Adverteerders', level: 1 })).toBeVisible();
  await expect(
    page.getByRole('row', {
      name: /Bakkerij De Test · Loil Advertentie Machtiging Piet van der Berg — — € 35,00 € 35,00 open/,
    }),
  ).toBeVisible();
  await expect(page.getByText('ALFRED ONBEKEND (niet gekoppeld)')).toBeVisible();
  await expectNoSeriousA11yIssues(page);

  await page.getByRole('link', { name: 'Garage Proef' }).click();
  await expect(page.getByText('Collectant in het Excel-bestand: ALFRED ONBEKEND.')).toBeVisible();
  await page.getByLabel('Collectant (kaderlid of rol Collectant)').selectOption({ label: 'Piet van der Berg' });
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Adverteerder opgeslagen.')).toBeVisible();
  expect(api.advertisers[1]).toMatchObject({ collectorMemberId: 'm-1', importedCollectorName: null });

  await page.goto('/beheer/adverteerders/adv-1');
  await expect(page.getByRole('heading', { name: 'Bijdragen per carnavalsjaar' })).toBeVisible();
  await expect(page.getByRole('img', { name: /Bijdragen 2025\/2026: € 35,00/ })).toBeVisible();
  await expect(page.getByText(/Nu: \*\*\*\* 4300/)).toBeVisible();
  await page.getByRole('button', { name: 'IBAN voluit tonen' }).click();
  await expect(page.getByText(/Nu: NL91ABNA0417164300/)).toBeVisible();
  await expectNoSeriousA11yIssues(page);
});

test('fase 27b: campagne met voortgangsbalk, filter op collectant en stand wijzigen', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  await api.install(page);
  await page.goto('/beheer/adverteerders/campagne');

  await expect(page.getByRole('heading', { name: 'Campagne 2026/2027', level: 1 })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Voortgang: 0%' })).toBeVisible();
  await expectNoSeriousA11yIssues(page);

  await page.getByLabel('Stand van Bakkerij De Test').selectOption({ label: 'Opgehaald' });
  await expect(page.getByRole('heading', { name: 'Voortgang: 50%' })).toBeVisible();
  expect(api.advertisers[0]!.status2027).toBe('Collected');

  // Contant: de garage wordt opgehaald en het geld is ontvangen (fase 27d).
  await page.getByLabel('Stand van Garage Proef').selectOption({ label: 'Opgehaald' });
  await expect(page.getByText('nog te ontvangen: € 70,00')).toBeVisible();
  await page.getByLabel('Contant ontvangen van Garage Proef').click();
  await expect(page.getByLabel('Contant ontvangen van Garage Proef')).toBeChecked();
  await expect(page.getByText('nog te ontvangen: € 0,00')).toBeVisible();
  expect(api.advertisers[1]!.paid2027).not.toBeNull();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exporteren (Excel)' }).click();
  expect((await download).suggestedFilename()).toBe('adverteerders-2027.xlsx');

  // Fase 27g: ronde per bedrijf; de opmerking van de collectant blijft staan als de stand verandert.
  await page.getByLabel('Ronde van Garage Proef').selectOption({ label: 'Ronde 2' });
  await expect(page.getByLabel('Ronde van Garage Proef')).toHaveValue('2');
  expect(api.advertisers[1]!.round2027).toBe(2);
  await expect(page.getByRole('cell', { name: 'Liever na 18 uur langskomen' })).toBeVisible();
  expect(api.advertisers[1]!.note2027).toBe('Liever na 18 uur langskomen');

  // Filteren op collectant via de tabel per collectant.
  await page.getByRole('button', { name: 'Piet van der Berg' }).click();
  await expect(page.getByLabel('Collectant')).toHaveValue('m-1');
  await expect(page.getByRole('link', { name: '2. Garage Proef' })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Voortgang: 100%' })).toBeVisible();
});

test('fase 27b: Excel eerst controleren en dan inlezen', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  await api.install(page);
  await page.goto('/beheer/adverteerders/import');

  await expect(page.getByRole('button', { name: 'Inlezen' })).toBeDisabled();
  await page.getByLabel('Excel-bestand').setInputFiles({
    name: 'Advertentie overzicht 2026.xlsm',
    mimeType: 'application/vnd.ms-excel',
    buffer: Buffer.from('x'),
  });
  await page.getByRole('button', { name: 'Controleren' }).click();
  await expect(page.getByText('187 regels: 185 nieuw, 2 bijgewerkt. Bijdragen uit 2025, 2026.')).toBeVisible();
  await expect(page.getByText('Regel 12: Kapsalon: machtiging zonder IBAN')).toBeVisible();
  await page.getByRole('button', { name: 'Inlezen' }).click();
  await expect(page.getByText('Ingelezen: 185 nieuw en 2 bijgewerkt.')).toBeVisible();
  expect(api.advertiserImports).toBe(1);
  await expectNoSeriousA11yIssues(page);
});

test('fase 27c: incasso van de opgehaalde adverteerders', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  await api.install(page);
  await page.goto('/beheer/adverteerders/incasso');

  await expect(page.getByRole('heading', { name: 'Incasso adverteerders 2026/2027', level: 1 })).toBeVisible();
  await page.getByLabel('Incassodatum').fill('2026-11-02');
  await expect(
    page.getByRole('region', { name: 'Adverteerders in de incasso' }).getByRole('link', { name: 'Bakkerij De Test' }),
  ).toBeVisible();
  await expect(page.getByRole('region', { name: 'Overgeslagen adverteerders' })).toContainText('Geen IBAN');
  await expectNoSeriousA11yIssues(page);

  await page.getByRole('button', { name: 'Incassorun maken (1)' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Run maken' }).click();
  await expect(page.getByText('De incassorun is gemaakt. Download het bestand hieronder.')).toBeVisible();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: /Bestand downloaden/ }).click();
  expect((await download).suggestedFilename()).toBe('incasso-adverteerders-20261102.xml');

  // Daarna komt dezelfde adverteerder niet nog een keer in de incasso.
  await expect(page.getByRole('button', { name: 'Incassorun maken (0)' })).toBeDisabled();
});

test('fase 27e: facturen maken, versturen en printen', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  api.advertisers.forEach((a) => (a.status2027 = 'Collected'));
  await api.install(page);
  await page.goto('/beheer/adverteerders/facturen');

  await expect(page.getByRole('heading', { name: 'Facturen 2026/2027', level: 1 })).toBeVisible();
  await expect(page.getByLabel('KvK-nummer')).toHaveValue('40122564');
  await expectNoSeriousA11yIssues(page);

  await page.getByRole('button', { name: 'Facturen maken (2)' }).click();
  await expect(page.getByText('2 facturen gemaakt.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'ADV-2027-0001' })).toBeVisible();

  await page.getByRole('button', { name: 'Versturen per e-mail (1)' }).click();
  await expect(page.getByRole('dialog')).toContainText('namens penningmeester@vrolijkedrammers.nl');
  await page.getByRole('dialog').getByRole('button', { name: 'Versturen' }).click();
  await expect(page.getByText('1 facturen worden verstuurd.')).toBeVisible();

  // De garage heeft geen e-mailadres: die factuur gaat mee in de PDF om te printen.
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'PDF zonder e-mailadres' }).click();
  expect((await download).suggestedFilename()).toBe('Facturen 2027 zonder e-mail.pdf');

  await page.getByLabel('Adres (één regel per regel)').fill('Postbus 123\n6940 AC Loil');
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Gegevens op de factuur opgeslagen.')).toBeVisible();
  expect(api.invoiceSettings.address).toBe('Postbus 123\n6940 AC Loil');
});
