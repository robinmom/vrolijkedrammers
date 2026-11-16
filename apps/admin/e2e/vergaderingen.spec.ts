import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Vergaderingen (2026-11-16): bestuur en raad van elf, met agenda, notulen en de agendamail in de huisstijl. */

async function open(page: Page, api: MockApi, path = '') {
  await api.install(page);
  await page.goto(`/beheer/${path}`);
}

async function expectNoSeriousA11yIssues(page: Page) {
  // Het voorbeeld van de mail staat in een iframe met een eigen CSP; dat is geen onderdeel van het portal zelf.
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
    .exclude('.mailing-frame')
    .analyze();
  const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(
    serious.map((v) => `${v.id}: ${v.nodes.map((n) => `${n.target.join(' ')} ${n.failureSummary}`).join(', ')}`),
  ).toEqual([]);
}

test('vergaderingen: alle vergaderingen bij elkaar, te filteren op soort', async ({ page }) => {
  const api = new MockApi(['meeting.manage', 'event.manage']);
  await open(page, api, 'vergaderingen');
  await expect(page.getByRole('link', { name: 'Bestuursvergadering' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Vergadering Raad van Elf' })).toBeVisible();
  await expect(page.getByText('Nog niet')).toBeVisible();

  await page.getByRole('button', { name: 'Raad van Elf', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Bestuursvergadering' })).toBeHidden();
  await expect(page.getByRole('link', { name: 'Vergadering Raad van Elf' })).toBeVisible();
  await expectNoSeriousA11yIssues(page);
});

test('vergaderingen: nieuwe vergadering met een eigen agenda-item', async ({ page }) => {
  const api = new MockApi(['meeting.manage', 'event.manage']);
  await open(page, api, 'vergaderingen/nieuw');
  await page.getByLabel('Voor wie').selectOption('Both');
  await expect(page.getByLabel('Titel')).toHaveValue('Gezamenlijke vergadering');
  await page.getByLabel('Begint').fill('2027-03-02T20:00');
  await page.getByLabel('Locatie').fill('De Duif');
  await page.getByRole('button', { name: 'Vergadering maken' }).click();

  const created = api.meetings.at(-1)!;
  expect(created).toMatchObject({ audience: 'Both', title: 'Gezamenlijke vergadering', locationName: 'De Duif' });
  await expect(page.getByRole('heading', { name: 'Agenda (0)' })).toBeVisible();
});

test('vergaderingen: agenda maken, notulen bijvoegen en de agenda versturen', async ({ page }) => {
  const api = new MockApi(['meeting.manage', 'event.manage']);
  await open(page, api, 'vergaderingen/v-1');
  await expect(page.getByRole('heading', { name: 'Bestuursvergadering', level: 1 })).toBeVisible();

  // Een punt toevoegen en de agenda opslaan.
  await page.getByRole('button', { name: 'Punt toevoegen' }).click();
  await page.getByLabel('Punt 3').fill('Begroting 2027');
  await page.getByLabel('Toelichting 3').fill('ter besluitvorming');
  await page.getByRole('button', { name: 'Agenda opslaan' }).click();
  await expect(page.getByText('Agenda opgeslagen.')).toBeVisible();
  expect(api.meetings[0]!.agendaItems.map((a) => a.title)).toEqual([
    'Opening',
    'Notulen vorige vergadering',
    'Begroting 2027',
  ]);

  // De notulen staan als document bij het agenda-item en gaan standaard mee met de mail.
  await expect(page.getByRole('link', { name: 'notulen-december.pdf' })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: /notulen-december\.pdf/ })).toBeChecked();
  await page.getByLabel('Document toevoegen (PDF, JPEG of PNG, max. 10 MB)').setInputFiles({
    name: 'agenda-januari.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.4'),
  });
  await expect(page.getByText('Document toegevoegd.')).toBeVisible();
  expect(api.meetings[0]!.documents.map((d) => d.fileName)).toEqual(['notulen-december.pdf', 'agenda-januari.pdf']);

  // Wie de agenda krijgt, staat erbij; iemand zonder e-mailadres wordt apart gemeld.
  await expect(page.getByText(/2 ontvangers/)).toBeVisible();
  await expect(page.getByText(/Chris Zonder Mail/)).toBeVisible();
  await page.getByLabel(/Extra adressen/).fill('penningmeester@example.com');

  await page.getByRole('button', { name: 'Voorbeeld' }).click();
  await expect(page.getByText(/Onderwerp:/)).toBeVisible();
  await expect(page.getByTitle('Voorbeeld van de agendamail')).toBeVisible();

  await page.getByRole('button', { name: 'Agenda versturen' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Versturen' }).click();
  await expect(page.getByText('Agenda verstuurd naar 3 ontvangers.')).toBeVisible();
  expect(api.audit[0]).toMatchObject({ action: 'meeting.agenda-sent' });
  expect(JSON.parse(api.audit[0]!.newValues!)).toMatchObject({ recipients: 3, attachments: 2 });
  await expectNoSeriousA11yIssues(page);
});

test('vergaderingen: agenda overnemen van de vorige vergadering', async ({ page }) => {
  const api = new MockApi(['meeting.manage', 'event.manage']);
  await open(page, api, 'vergaderingen/v-2');
  await expect(page.getByRole('heading', { name: 'Agenda (0)' })).toBeVisible();
  await page.getByRole('button', { name: 'Overnemen van vorige vergadering' }).click();
  await expect(page.getByText('Agenda van de vorige vergadering overgenomen.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Agenda (2)' })).toBeVisible();
  await expect(page.getByLabel('Punt 1')).toHaveValue('Opening');
});
