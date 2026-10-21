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

  // Zoeken en filteren op stand en ronde; stopt wordt lichtrood.
  await page.getByLabel('Stand van Bakkerij De Test').selectOption({ label: 'Stopt' });
  await expect(page.getByRole('row', { name: /Bakkerij De Test/ })).toHaveCSS('background-color', 'rgb(244, 204, 204)');
  await page.getByLabel('Zoeken').fill('garage');
  await expect(page.getByRole('link', { name: 'Bakkerij De Test' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Garage Proef' })).toBeVisible();
  await page.getByLabel('Zoeken').fill('');
  await page.getByLabel('Stand', { exact: true }).selectOption({ label: 'Stopt' });
  await expect(page.getByRole('link', { name: 'Garage Proef' })).toHaveCount(0);
  await page.getByLabel('Stand', { exact: true }).selectOption({ label: 'Alle standen' });
  await page.getByLabel('Ronde', { exact: true }).selectOption({ label: 'Ronde 2' });
  await expect(page.getByRole('link', { name: 'Bakkerij De Test' })).toHaveCount(0);
  await expect(page.getByRole('row', { name: /Garage Proef/ })).toHaveCSS('background-color', 'rgb(221, 235, 247)');
  await page.getByLabel('Ronde', { exact: true }).selectOption({ label: 'Alle rondes' });

  // Filteren op collectant via de tabel per collectant.
  await page.getByRole('button', { name: 'Piet van der Berg' }).click();
  await expect(page.getByLabel('Collectant', { exact: true })).toHaveValue('m-1');
  await expect(page.getByRole('link', { name: 'Garage Proef' })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Voortgang: 100%' })).toBeVisible();
});

test('fase 27h/i: collectieperiode, informatie en herinneringen', async ({ page }) => {
  const api = new MockApi(['report.view', 'advertiser.manage']);
  await api.install(page);

  // Dashboard: alleen de voortgangsmeter van de campagne.
  await page.goto('/beheer/');
  await expect(page.getByRole('heading', { name: 'Adverteerders 2026/2027: 0%' })).toBeVisible();
  await expect(page.getByRole('progressbar', { name: 'Adverteerders afgehandeld' })).toHaveAttribute(
    'aria-valuenow',
    '0',
  );

  await page.goto('/beheer/adverteerders/campagne');

  await expect(page.getByRole('heading', { name: 'Collectieperiode en informatie 2026/2027' })).toBeVisible();
  await page.getByLabel('Startdatum').fill('2026-10-12');
  await page.getByLabel('Einddatum').fill('2026-12-01');
  await page.getByRole('textbox', { name: 'Informatie' }).fill('Inleveren: uiterlijk 1 december 2026');
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Opgeslagen.')).toBeVisible();
  expect(api.advertiserInfo[2027]).toEqual({
    text: 'Inleveren: uiterlijk 1 december 2026',
    startDate: '2026-10-12',
    endDate: '2026-12-01',
  });

  // Fase 27i: herinnering aan één collectant en aan iedereen met open adverteerders.
  await page.getByRole('button', { name: 'Herinnering naar Piet van der Berg' }).click();
  await expect(page.getByText('Herinnering verstuurd naar 1 collectant.')).toBeVisible();
  expect(api.advertiserReminders).toEqual([['m-1']]);
  await page.getByRole('button', { name: 'Herinnering aan iedereen met open adverteerders' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Versturen' }).click();
  await expect(page.getByText('Herinnering verstuurd naar 1 collectant.')).toBeVisible();
  expect(api.advertiserReminders).toEqual([['m-1'], null]);
});

test('fase 27j: campagne sorteren op een kolom', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  await api.install(page);
  await page.goto('/beheer/adverteerders/campagne');

  const table = page.getByRole('region', { name: 'Adverteerders in de campagne' });
  const companies = table.getByRole('link');
  await expect(companies).toHaveText(['Bakkerij De Test', 'Garage Proef']);

  // Vorig jaar: eerst oplopend, nog een klik aflopend.
  await table.getByRole('button', { name: 'Vorig jaar' }).click();
  await expect(table.getByRole('columnheader', { name: /Vorig jaar/ })).toHaveAttribute('aria-sort', 'ascending');
  await expect(companies).toHaveText(['Bakkerij De Test', 'Garage Proef']);
  await table.getByRole('button', { name: /Vorig jaar/ }).click();
  await expect(table.getByRole('columnheader', { name: /Vorig jaar/ })).toHaveAttribute('aria-sort', 'descending');
  await expect(companies).toHaveText(['Garage Proef', 'Bakkerij De Test']);

  // Bedrijf aflopend op naam.
  await table.getByRole('button', { name: 'Bedrijf' }).click();
  await table.getByRole('button', { name: /Bedrijf/ }).click();
  await expect(companies).toHaveText(['Garage Proef', 'Bakkerij De Test']);
});

test('fase 27m: pagina als eigen kolom, direct aan te passen', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  await api.install(page);
  await page.goto('/beheer/adverteerders/campagne');

  const table = page.getByRole('region', { name: 'Adverteerders in de campagne' });
  // De pagina staat in een eigen kolom (niet meer vóór de naam), met de waarde van de adverteerder.
  await expect(table.getByRole('columnheader', { name: 'Pagina' })).toBeVisible();
  const pagina = table.getByLabel('Pagina van Bakkerij De Test');
  await expect(pagina).toHaveValue('4');

  // Inline wijzigen slaat op zodra het veld de focus verliest.
  await pagina.fill('12');
  await pagina.blur();
  await expect.poll(() => api.advertisers.find((a) => a.id === 'adv-1')?.page).toBe('12');
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

  // Fase 27o: de betaalstatus staat in de lijst en kan vernieuwd worden (Mollie → betaald).
  await expect(page.getByRole('button', { name: '↻ status vernieuwen' }).first()).toBeVisible();
  await page.getByRole('button', { name: '↻ status vernieuwen' }).first().click();
  await expect(page.locator('.badge', { hasText: 'Betaald' })).toBeVisible();

  // De garage heeft geen e-mailadres: die factuur gaat mee in de PDF om te printen.
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'PDF zonder e-mailadres' }).click();
  expect((await download).suggestedFilename()).toBe('Facturen 2027 zonder e-mail.pdf');

  await page.getByLabel('Adres (één regel per regel)').fill('Postbus 123\n6940 AC Loil');

  // Fase 27n: btw-nummer en de servicekosten-switch.
  await page.getByLabel('Btw-nummer').fill('NL005844824B01');
  await page.getByLabel('Servicekosten doorberekenen').check();
  await page.getByLabel('Servicekosten (€, excl. btw)').fill('0.35');
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Gegevens op de factuur opgeslagen.')).toBeVisible();
  expect(api.invoiceSettings.address).toBe('Postbus 123\n6940 AC Loil');
  expect(api.invoiceSettings.vatNumber).toBe('NL005844824B01');
  expect(api.invoiceSettings.serviceFeeOn).toBe(true);
  expect(api.invoiceSettings.serviceFeeCents).toBe(35);
});

test('fase 27p: factuur per adverteerder maken', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  api.advertisers.forEach((a) => (a.status2027 = 'Collected'));
  await api.install(page);
  await page.goto('/beheer/adverteerders/facturen');

  // Elke opgehaalde adverteerder zonder factuur heeft een eigen "Factuur maken"-knop.
  const makeButtons = page.getByRole('button', { name: 'Factuur maken' });
  await expect(makeButtons).toHaveCount(2);
  await makeButtons.first().click();

  // Eén factuur is nu gemaakt (nummer verschijnt), de andere rij houdt zijn knop.
  await expect(page.getByRole('button', { name: /ADV-2027-/ })).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Factuur maken' })).toHaveCount(1);
});

test('fase 27q: betaaltermijn, vervallen filteren en herinnering sturen', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  api.advertisers.forEach((a) => (a.status2027 = 'Collected'));
  await api.install(page);
  await page.goto('/beheer/adverteerders/facturen');

  // Betalingstermijn instellen.
  await page.getByLabel('Betalingstermijn (dagen)').fill('21');
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Gegevens op de factuur opgeslagen.')).toBeVisible();
  expect(api.invoiceSettings.paymentTermDays).toBe(21);

  // Facturen maken en versturen (alleen wie een e-mailadres heeft).
  await page.getByRole('button', { name: /Facturen maken/ }).click();
  await page.getByRole('button', { name: /Versturen per e-mail/ }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Versturen' }).click();
  await expect(page.getByText(/facturen worden verstuurd/)).toBeVisible();

  // Maak de verstuurde factuur vervallen en herlaad.
  api.invoices.find((i) => i.status === 'Sent')!.overdue = true;
  await page.reload();

  // Filter op Vervallen: de factuur is zichtbaar met de badge "Vervallen" en selecteerbaar.
  await page.getByLabel('Tonen').selectOption('overdue');
  await expect(page.locator('.badge', { hasText: 'Vervallen' })).toBeVisible();
  await page
    .getByRole('checkbox', { name: /Selecteer factuur/ })
    .first()
    .check();
  await page.getByRole('button', { name: /Herinnering sturen/ }).click();
  await expect(page.getByText(/herinneringen worden verstuurd/)).toBeVisible();
  expect(api.invoiceReminders.length).toBe(1);
});

test('fase 27k: context-bewuste terug-knop vanaf Campagne', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  await api.install(page);
  await page.goto('/beheer/adverteerders/campagne');

  // Vanaf Campagne op een bedrijf klikken en teruggaan komt terug op Campagne (niet op Overzicht).
  await page.getByRole('link', { name: 'Bakkerij De Test' }).click();
  await expect(page).toHaveURL(/\/adverteerders\/adv-1\?from=campagne$/);
  await expect(page.getByRole('heading', { name: 'Bakkerij De Test', level: 1 })).toBeVisible();
  await page.getByRole('link', { name: '← Campagne' }).click();
  await expect(page.getByRole('heading', { name: 'Campagne 2026/2027', level: 1 })).toBeVisible();

  // Vanaf Overzicht gaat de terug-knop naar Overzicht.
  await page.goto('/beheer/adverteerders');
  await page.getByRole('link', { name: 'Bakkerij De Test' }).click();
  await expect(page.getByRole('link', { name: '← Adverteerders' })).toBeVisible();
});

test('fase 27k: selecteren en in één keer toewijzen aan een andere collectant', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  await api.install(page);
  await page.goto('/beheer/adverteerders/campagne');

  // Filter op één collectant; alleen zijn bedrijf blijft over.
  await page.getByLabel('Collectant', { exact: true }).selectOption({ label: 'Piet van der Berg' });
  await expect(page.getByRole('link', { name: 'Bakkerij De Test' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Garage Proef' })).toHaveCount(0);

  // Alles selecteren: de actiebalk verschijnt met wie ze nu zijn.
  await page.getByLabel('Alles selecteren').check();
  await expect(page.getByLabel('Selecteer Bakkerij De Test')).toBeChecked();
  await expect(page.getByText('1 bedrijf geselecteerd (nu van Piet van der Berg).')).toBeVisible();
  await expectNoSeriousA11yIssues(page);

  // Toewijzen aan een ander en bevestigen.
  await page.getByLabel('Toewijzen aan').selectOption({ label: 'Karin Kader' });
  await page.getByRole('button', { name: 'Wijs toe aan…' }).click();
  await expect(page.getByRole('dialog')).toContainText(
    '1 bedrijf overzetten naar Karin Kader (nu van Piet van der Berg)?',
  );
  await page.getByRole('dialog').getByRole('button', { name: 'Toewijzen' }).click();

  await expect(page.getByText('1 bedrijf toegewezen aan Karin Kader.')).toBeVisible();
  expect(api.advertisers[0]!.collectorMemberId).toBe('m-2');
  // De lijst (nog gefilterd op Piet van der Berg) is nu leeg.
  await expect(page.getByRole('link', { name: 'Bakkerij De Test' })).toHaveCount(0);
});
