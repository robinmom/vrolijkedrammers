import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 12c: vaste plekken vooraan, export in het deelnemersformaat, startnummers importeren en de vraag Muziek. */

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

test('fase 12c: vaste plekken vooraan aanpassen', async ({ page }) => {
  const api = new MockApi(['parade.config']);
  api.parades.push({
    id: 'p-1',
    carnivalYearId: 1,
    name: 'Optocht Loil 2027',
    paradeDate: '2027-02-07',
    startTime: '13:30:00',
    startLocation: null,
    routeDescription: null,
    routeLengthKm: null,
    registrationOpensAt: '2026-12-01T08:00:00Z',
    registrationClosesAt: '2027-01-20T22:59:00Z',
    editDeadlineAt: null,
    subjectRequired: true,
    defaultSpacingMeters: 5,
    maxDocumentsPerRegistration: 5,
    maxDocumentSizeMb: 10,
    status: 'RegistrationOpen',
    infoText: null,
    fixedEntries: [
      { name: 'Geluidswagen', adultCount: 2, childrenCount: 0, hasMusic: true },
      { name: 'Verenigingswagen', adultCount: 14, childrenCount: 0, hasMusic: true },
      { name: 'Het Convent', adultCount: 8, childrenCount: 0, hasMusic: false },
    ],
  });
  await open(page, api, 'optocht');
  const card = page.getByRole('region', { name: 'Vaste plekken vooraan' });
  await expect(card.getByText('de groepen beginnen bij 4')).toBeVisible();
  await expectNoSeriousA11yIssues(page);

  await card.getByLabel('Naam vaste plek 3').fill('Het Convent van "de Vrolijke Drammers"');
  await card.getByRole('button', { name: 'Vaste plek 3 omhoog' }).click();
  await card.getByRole('button', { name: '+ Vaste plek' }).click();
  await card.getByLabel('Naam vaste plek 4').fill('Prinsenwagen');
  await card.getByLabel('Muziek bij vaste plek 4').check();
  await card.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Vaste plekken opgeslagen.')).toBeVisible();
  expect(api.lineupCalls.at(-1)).toMatchObject({
    path: '/admin/parades/p-1/fixed-entries',
    body: {
      entries: [
        { name: 'Geluidswagen' },
        { name: 'Het Convent van "de Vrolijke Drammers"', hasMusic: false },
        { name: 'Verenigingswagen' },
        { name: 'Prinsenwagen', hasMusic: true, adultCount: 0 },
      ],
    },
  });
});

test('fase 12c: exporteren en startnummers importeren met voorbeeld', async ({ page }) => {
  const api = new MockApi(['parade.read', 'parade.manage', 'parade.assign-start-number', 'parade.export']);
  api.importPreview = {
    version: 4,
    rows: 3,
    changes: [],
    errors: [
      { row: 5, message: 'Startnummer 2 bij opgave 1 is van de vaste plek "Verenigingswagen".' },
      { row: 8, message: 'Opgave 42 bestaat niet in deze optocht.' },
    ],
  };
  await open(page, api, 'optocht/inschrijvingen');

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exporteren (Excel)' }).click();
  expect((await download).suggestedFilename()).toBe('Opgaven optocht 2027 29-9-2026.xlsx');

  await page.getByRole('button', { name: 'Startnummers importeren' }).click();
  const dialog = page.getByRole('dialog', { name: 'Startnummers importeren' });
  await dialog.getByLabel('Excel-bestand (.xlsx)').setInputFiles({
    name: 'startnummers.xlsx',
    mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    buffer: Buffer.from('xlsx'),
  });
  await expect(dialog.getByText('2 fouten: er wordt niets ingelezen.')).toBeVisible();
  await expect(dialog.getByText('Regel 8: Opgave 42 bestaat niet in deze optocht.')).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Inlezen' })).toBeDisabled();
  await expectNoSeriousA11yIssues(page);

  api.importPreview = {
    version: 4,
    rows: 3,
    changes: [
      { registrationNumber: 2, groupName: 'De Bouwers', oldStartNumber: null, newStartNumber: 4, published: false },
      { registrationNumber: 1, groupName: 'De Knotwilgen', oldStartNumber: 5, newStartNumber: 7, published: true },
    ],
    errors: [],
  };
  await dialog.getByLabel('Excel-bestand (.xlsx)').setInputFiles({
    name: 'startnummers.xlsx',
    mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    buffer: Buffer.from('xlsx2'),
  });
  await expect(dialog.getByRole('cell', { name: 'De Knotwilgen gepubliceerd' })).toBeVisible();
  await expect(dialog.getByText('Groepen met een gepubliceerd startnummer krijgen een melding')).toBeVisible();
  await dialog.getByRole('button', { name: '2 wijziging(en) inlezen' }).click();
  await expect(page.getByText('2 startnummer(s) ingelezen.')).toBeVisible();
  expect(api.lineupCalls.map((c) => c.path)).toEqual([
    '/admin/parade/export',
    '/admin/parade/start-numbers/import/preview',
    '/admin/parade/start-numbers/import/preview',
    '/admin/parade/start-numbers/import',
  ]);
});

test('fase 12c: zonder exportrecht geen exportknop; muziek bij een inschrijving', async ({ page }) => {
  const api = new MockApi(['parade.read']);
  await open(page, api, 'optocht/inschrijvingen');
  await expect(page.getByRole('heading', { name: 'Optochtinschrijvingen' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Exporteren (Excel)' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Startnummers importeren' })).toHaveCount(0);
  await page.goto('/beheer/optocht/inschrijvingen/r-1');
  const music = page.locator('dt', { hasText: 'Muziek' });
  await expect(music.locator('xpath=following-sibling::dd[1]')).toHaveText('Ja');
});

test('fase 12c: samenstellen toont de vaste plekken en begint bij het eerste vrije startnummer', async ({ page }) => {
  const api = new MockApi(['parade.read', 'parade.manage', 'parade.assign-start-number']);
  await open(page, api, 'optocht/samenstellen');
  const fixed = page.getByRole('list', { name: 'Vaste plekken vooraan' });
  await expect(fixed.getByText('Geluidswagen')).toBeVisible();
  await expect(fixed.getByText('Vaste plek')).toHaveCount(2);
  await expect(page.getByLabel(/Eerste startnummer/)).toHaveValue('3');
});
