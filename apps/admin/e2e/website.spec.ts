import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 21a: menukop Website (homepage, pagina's, kader, prinsen, onderscheidingen, instellingen) en nieuws op de website. */

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

const tinyPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

test('fase 21a: menukop Website met alle onderdelen', async ({ page }) => {
  const api = new MockApi(['website.manage']);
  await open(page, api, 'website/homepage');
  await expect(page.getByRole('heading', { name: 'Homepage', level: 1 })).toBeVisible();
  const toggle = page.getByRole('button', { name: 'Menu' });
  if (await toggle.isVisible()) await toggle.click();
  const nav = page.getByRole('navigation');
  await expect(nav.getByText('Website', { exact: true })).toBeVisible();
  for (const name of ['Homepage', "Pagina's", 'Kader', 'Prinsen', 'Onderscheidingen', 'Instellingen website']) {
    await expect(nav.getByRole('link', { name })).toBeVisible();
  }
});

test('fase 21a: hero aanpassen met een nieuwe foto', async ({ page }) => {
  const api = new MockApi(['website.manage']);
  await open(page, api, 'website/homepage');
  await page.getByLabel('Titel', { exact: true }).fill('Loil geet los!');
  await page.getByLabel(/^Hero-foto/).setInputFiles({ name: 'hero.png', mimeType: 'image/png', buffer: tinyPng });
  await expect(page.locator('.image-picker img')).toBeVisible();
  await page.getByLabel('Knop 2: gaat naar').selectOption('Tickets');
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Opgeslagen.')).toBeVisible();
  expect(api.websiteSettings.heroTitle).toBe('Loil geet los!');
  expect(api.websiteSettings.heroSecondaryLink).toBe('Tickets');
  expect(api.websiteSettings.heroImageUrl).toContain('data:image/png');
  await expectNoSeriousA11yIssues(page);
});

test('fase 21a: kaderlid uit de ledenlijst toevoegen en de volgorde wijzigen', async ({ page }) => {
  const api = new MockApi(['website.manage']);
  await open(page, api, 'website/kader');
  await expect(page.getByRole('tab', { name: /Bestuur/ })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('row', { name: /Marcel Wiendels Voorzitter Lid 0031/ })).toBeVisible();

  await page.getByRole('button', { name: 'Anouk Berendsen omhoog' }).click();
  await expect(page.getByText('Volgorde opgeslagen.')).toBeVisible();
  const rows = page.getByRole('tabpanel').getByRole('row');
  await expect(rows.nth(1)).toContainText('Anouk Berendsen');

  await page.getByRole('button', { name: 'Kaderlid toevoegen' }).click();
  const dialog = page.getByRole('dialog', { name: 'Kaderlid toevoegen' });
  await dialog.getByLabel('Zoek in de ledenlijst').fill('anna');
  await dialog.getByRole('button', { name: /Anna Jansen/ }).click();
  await expect(dialog.getByLabel('Naam op de website')).toHaveValue('Anna Jansen');
  await dialog.getByLabel('Functie (op de website)').fill('Secretaris');
  await dialog.getByLabel(/^Pasfoto/).setInputFiles({ name: 'pasfoto.png', mimeType: 'image/png', buffer: tinyPng });
  await expect(dialog.locator('img.preview-round')).toBeVisible();
  await expectNoSeriousA11yIssues(page);
  await dialog.getByRole('button', { name: 'Toevoegen' }).click();
  await expect(page.getByRole('row', { name: /Anna Jansen Secretaris Lid 002/ })).toBeVisible();
  expect(api.kader.at(-1)).toMatchObject({ memberId: 'm-2', function: 'Secretaris' });
});

test('fase 21a: prinsen en een aparte pagina jeugdprinsen die je pas zichtbaar zet als hij gevuld is', async ({
  page,
}) => {
  const api = new MockApi(['website.manage']);
  await open(page, api, 'website/prinsen');
  await expect(page.getByRole('row', { name: /2025 Prins Ronnie I Ronnie Loeters/ })).toBeVisible();

  await page.getByRole('tab', { name: 'Jeugdprinsen' }).click();
  await expect(page.getByText('Nog geen jeugdprinsen.')).toBeVisible();
  await page.getByRole('button', { name: 'Jeugdprins(es) toevoegen' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Jaar (carnaval)').fill('2025');
  await dialog.getByLabel('Prinsennaam').fill('Jeugdprinses Eva I');
  await dialog.getByLabel('Motto').fill('Alaaf!');
  await dialog.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByRole('row', { name: /Jeugdprinses Eva I/ })).toBeVisible();
  expect(api.princes.at(-1)).toMatchObject({ kind: 'YouthPrince', year: 2025, motto: 'Alaaf!' });

  await page.getByLabel('Pagina Jeugdprinsen tonen op de website').check();
  await expect(page.getByText('De pagina Jeugdprinsen staat online.')).toBeVisible();
  expect(api.websiteSettings.showYouthPrinces).toBe(true);
  await expectNoSeriousA11yIssues(page);
});

test('fase 21a: onderscheiding toevoegen, filteren en verwijderen', async ({ page }) => {
  const api = new MockApi(['website.manage']);
  await open(page, api, 'website/onderscheidingen');
  await page.getByRole('button', { name: 'Onderscheiding toevoegen' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Jaar').fill('2026');
  await dialog.getByLabel('Soort').selectOption('EikenloofVanBoschslag');
  await dialog.getByLabel('Ontvanger').fill('Peter Bosman');
  await dialog.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByRole('row', { name: /2026 Het Eikenloof van Boschslag Peter Bosman Online/ })).toBeVisible();

  await page.getByRole('button', { name: "'t Drammertje" }).click();
  await expect(page.getByRole('row', { name: /Peter Bosman/ })).toHaveCount(0);
  await expect(page.getByRole('row', { name: /Harrie Sloot/ })).toBeVisible();

  await page.getByRole('button', { name: 'Verwijderen Harrie Sloot' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Verwijderen' }).click();
  await expect(page.getByText('Nog geen onderscheidingen.')).toBeVisible();
  await expectNoSeriousA11yIssues(page);
});

test('fase 21a: pagina maken en instellingen voor social media', async ({ page }) => {
  const api = new MockApi(['website.manage']);
  await open(page, api, 'website/paginas');
  await expect(page.getByRole('row', { name: /Over ons \/over-ons Online/ })).toBeVisible();
  await page.getByRole('link', { name: 'Pagina toevoegen' }).click();
  await page.getByLabel('Titel').fill('Loillands');
  await page.getByLabel('Webadres').fill('loillands');
  await page.getByLabel('Tekst (Markdown)').fill('Festival in september.');
  await page.getByLabel('Online (zichtbaar op de website)').check();
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Pagina opgeslagen.')).toBeVisible();
  expect(api.websitePages.at(-1)).toMatchObject({ slug: 'loillands', isPublished: true });

  await page.goto('/beheer/website/instellingen');
  await page.getByLabel('Instagram').fill('https://www.instagram.com/vrolijkedrammers');
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Opgeslagen.')).toBeVisible();
  expect(api.websiteSettings.instagramUrl).toBe('https://www.instagram.com/vrolijkedrammers');
  await expectNoSeriousA11yIssues(page);
});

test('fase 21a: nieuws met afbeelding vóór het opslaan en ook op de website', async ({ page }) => {
  const api = new MockApi(['news.manage']);
  await open(page, api, 'nieuws/nieuw');
  await page.getByLabel('Titel').fill('Drammertje 2026');
  await page.getByLabel('Bericht (Markdown)').fill('Tekst voor de app.');
  await page
    .getByLabel(/^Afbeelding \(JPEG/)
    .setInputFiles({ name: 'foto.png', mimeType: 'image/png', buffer: tinyPng });
  await expect(page.locator('.image-picker img')).toBeVisible();
  await page.getByLabel(/Ook tonen op de website/).check();
  await page.getByLabel(/Langere tekst voor de website/).fill('Het hele verhaal.');
  await expectNoSeriousA11yIssues(page);
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Bericht opgeslagen.')).toBeVisible();
  expect(api.news[0]).toMatchObject({
    title: 'Drammertje 2026',
    image: 'uploads/0123456789abcdef0123456789abcdef.jpg',
    showOnWebsite: true,
    websiteBody: 'Het hele verhaal.',
  });
});

test('fase 21e: oude website overzetten met voortgang en mislukte onderdelen opnieuw proberen', async ({ page }) => {
  const api = new MockApi(['website.manage']);
  await open(page, api, 'website/instellingen');
  const card = page.getByRole('region', { name: 'Oude website overzetten' });
  await card.getByRole('button', { name: 'Start overzetten' }).click();
  await expect(card.getByRole('row', { name: /Nieuwsberichten 120/ })).toBeVisible();
  await expect(card.getByText('Klaar')).toBeVisible();
  await card.getByText('Wat is er mislukt? (1)').click();
  await expect(card.getByText('Oude pagina: Niet gevonden')).toBeVisible();
  await expectNoSeriousA11yIssues(page);
  await card.getByRole('button', { name: 'Mislukte opnieuw proberen' }).click();
  await expect(card.getByText(/Wat is er mislukt/)).toBeHidden();
  await expect(card.getByRole('button', { name: 'Opnieuw controleren' })).toBeVisible();
  expect(api.audit.map((a) => a.action)).toContain('website.import-started');
});
