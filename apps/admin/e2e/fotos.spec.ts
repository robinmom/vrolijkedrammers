import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 21b: galerijen per soort, bulk-upload met voortgang en bulkacties op foto's. */

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

const png = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

test('fase 21b: galerijen als kaarten, te filteren op soort', async ({ page }) => {
  const api = new MockApi(['photo.manage']);
  await open(page, api, 'fotos');
  const galleries = page.getByRole('list', { name: 'Galerijen' });
  await expect(galleries.getByRole('link', { name: /Optocht 2027/ })).toBeVisible();
  await page.getByRole('button', { name: 'Pronkzitting' }).click();
  await expect(galleries.getByRole('link')).toHaveCount(0);
  await expect(page.getByText('Nog geen galerijen van deze soort.')).toBeVisible();
  await page.getByRole('button', { name: 'Optocht' }).click();
  await expect(galleries.getByRole('link', { name: /Optocht 2027/ })).toBeVisible();
  await expectNoSeriousA11yIssues(page);
});

test('fase 21b: galerij maken en veel foto’s tegelijk uploaden met voortgang', async ({ page }) => {
  const api = new MockApi(['photo.manage']);
  await open(page, api, 'fotos/nieuw');
  await page.getByLabel('Titel').fill('Pronkzitting 2027');
  await page.getByLabel('Soort').selectOption('Pronkzitting');
  await page.getByRole('button', { name: 'Galerij maken' }).click();
  await expect(page.getByRole('heading', { name: 'Pronkzitting 2027', level: 1 })).toBeVisible();
  expect(api.albums.at(-1)).toMatchObject({ title: 'Pronkzitting 2027', category: 'Pronkzitting' });

  const files = Array.from({ length: 7 }, (_, i) => ({ name: `IMG_${i + 1}.png`, mimeType: 'image/png', buffer: png }));
  await page
    .getByLabel("Kies foto's")
    .setInputFiles([...files, { name: 'notities.txt', mimeType: 'text/plain', buffer: Buffer.from('x') }]);
  await expect(page.getByText('1 bestand(en) overgeslagen: geen JPEG, PNG of WebP.')).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: '7 geüpload.' })).toBeVisible();
  await expect(page.getByRole('heading', { name: "Foto's (7)" })).toBeVisible();
  expect(api.albums.at(-1)!.photos).toHaveLength(7);
  await expectNoSeriousA11yIssues(page);
});

test('fase 21b: foto’s selecteren en in één keer verbergen, verplaatsen en verwijderen', async ({ page }) => {
  const api = new MockApi(['photo.manage']);
  api.albums.push({
    ...structuredClone(api.albums[0]!),
    id: 'a-2',
    title: 'Carnaval zondag',
    category: 'Carnival',
    photos: [],
  });
  const album = api.albums[0]!;
  album.photos = ['p-1', 'p-2', 'p-3', 'p-4'].map((id) => ({ ...album.photos[0]!, id }));
  await open(page, api, 'fotos/a-1');

  await page.getByLabel('Foto 1 selecteren').check();
  await page.getByLabel('Foto 2 selecteren').check();
  const bar = page.getByRole('region', { name: "Acties voor de geselecteerde foto's" });
  await expect(bar.getByText('2 geselecteerd')).toBeVisible();
  await bar.getByRole('button', { name: 'Verbergen' }).click();
  await expect(page.getByText("2 foto('s) verborgen.")).toBeVisible();
  expect(album.photos.filter((p) => p.hidden).map((p) => p.id)).toEqual(['p-1', 'p-2']);

  await bar.getByLabel('Verplaatsen naar galerij').selectOption({ label: 'Carnaval zondag' });
  await bar.getByRole('button', { name: 'Verplaatsen' }).click();
  await expect(page.getByText("2 foto('s) verplaatst.")).toBeVisible();
  expect(api.albums[1]!.photos.map((p) => p.id)).toEqual(['p-1', 'p-2']);
  await expect(page.getByRole('heading', { name: "Foto's (2)" })).toBeVisible();

  await page.getByRole('button', { name: 'Alles selecteren' }).click();
  await bar.getByRole('button', { name: 'Verwijderen' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Verwijderen' }).click();
  await expect(page.getByText("Nog geen foto's in deze galerij.")).toBeVisible();
  expect(api.audit.map((a) => a.action)).toEqual(['photo.bulk-delete', 'photo.bulk-move', 'photo.bulk-hide']);
  await expectNoSeriousA11yIssues(page);
});
