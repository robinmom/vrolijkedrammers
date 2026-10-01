import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { forms, serveWebsitePages } from './website-page';

/** Fase 19b: /kaarten (sinds 21d een pagina van de website) — bestellen, vol → wachtlijst, en de bestelling met de QR. */

const products = {
  isMember: false,
  group: null,
  products: [
    {
      id: 'p-vr',
      kind: 'Pronkzitting',
      name: 'Pronkzitting vrijdag',
      description: null,
      eventId: null,
      date: '2027-02-05',
      priceCents: 1250,
      capacity: 300,
      remaining: 0,
      soldOut: true,
      maxPerOrder: 10,
      saleClosesAt: null,
      membersOnly: false,
      groupOrders: true,
    },
    {
      id: 'p-dag',
      kind: 'DayTicket',
      name: 'Dagkaart zaterdag',
      description: 'Voor gasten',
      eventId: null,
      date: '2027-02-13',
      priceCents: 750,
      capacity: 500,
      remaining: 120,
      soldOut: false,
      maxPerOrder: 10,
      saleClosesAt: null,
      membersOnly: false,
      groupOrders: false,
    },
    {
      id: 'p-mu',
      kind: 'Tokens',
      name: 'Consumptiemunten',
      description: null,
      eventId: null,
      date: null,
      priceCents: 250,
      capacity: null,
      remaining: null,
      soldOut: false,
      maxPerOrder: 100,
      saleClosesAt: null,
      membersOnly: true,
      groupOrders: false,
    },
  ],
};

async function serve(page: Page) {
  const posts: { path: string; body: unknown }[] = [];
  await serveWebsitePages(page, forms.kaarten);
  await page.route('**/api/v1/sales/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname.replace('/api/v1/sales', '');
    if (request.method() === 'POST') posts.push({ path, body: request.postDataJSON() });
    if (path === '/products') return route.fulfill({ json: products });
    if (path === '/orders') {
      return route.fulfill({
        status: 201,
        json: {
          orderId: 'o-1',
          number: '2027-0200',
          token: 'geheim',
          status: 'AwaitingPayment',
          checkoutUrl: '/kaarten/bestelling/?id=o-1&t=geheim',
        },
      });
    }
    if (path === '/waitlist') return route.fulfill({ status: 201, json: { id: 'w-1' } });
    if (path === '/orders/o-1') {
      return route.fulfill({
        json: {
          id: 'o-1',
          number: '2027-0200',
          status: 'Confirmed',
          kind: 'DayTicket',
          productName: 'Dagkaart zaterdag',
          date: '2027-02-13',
          groupName: null,
          memberQuantity: 0,
          paidQuantity: 2,
          amountCents: 1500,
          buyerName: 'Jan Jansen',
          createdAt: '2026-10-12T18:00:00Z',
          holdUntil: null,
          tickets: [{ id: 't-1', quantity: 2, status: 'Active', code: 'X', canShare: false }],
          sharedBy: null,
          sharedWith: [],
        },
      });
    }
    if (path === '/orders/o-1/tickets/t-1/qr.svg') {
      return route.fulfill({
        contentType: 'image/svg+xml',
        body: '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"/>',
      });
    }
    return route.fulfill({ status: 404, json: {} });
  });
  return posts;
}

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual(
    [],
  );
}

test('fase 19b: webpagina kaarten, bestellen en daarna de QR', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  const posts = await serve(page);
  await page.goto('/kaarten/');
  await expect(page.getByRole('heading', { name: 'Dagkaart zaterdag' })).toBeVisible();
  // Munten zijn alleen voor leden (in de app).
  await expect(page.getByRole('heading', { name: 'Consumptiemunten' })).toHaveCount(0);
  await expectNoSeriousA11yIssues(page);

  await page
    .getByRole('article')
    .filter({ hasText: 'Dagkaart zaterdag' })
    .getByRole('button', { name: 'Bestellen' })
    .click();
  await page.getByLabel('Aantal kaarten').fill('2');
  await expect(page.getByText('€ 15,00')).toBeVisible();
  await page.getByLabel('Naam').fill('Jan Jansen');
  await page.getByLabel('E-mailadres').fill('jan@example.com');
  await expectNoSeriousA11yIssues(page);
  await page.getByRole('button', { name: 'Betalen met iDEAL' }).click();

  await expect(page.getByRole('heading', { name: 'Dagkaart zaterdag · 2027-0200' })).toBeVisible();
  await expect(page.getByText('Betaald.', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Betalen met iDEAL' })).toBeHidden();
  await expect(page.getByRole('link', { name: 'Terug naar de app' })).toBeHidden();
  await expect(page.getByRole('img', { name: 'QR-code voor 2 personen' })).toBeVisible();
  expect(posts[0]).toMatchObject({
    path: '/orders',
    body: { productId: 'p-dag', paidQuantity: 2, buyerName: 'Jan Jansen', channel: 'Web' },
  });
  await expectNoSeriousA11yIssues(page);
  expect(errors.filter((e) => e.includes('Content Security Policy'))).toEqual([]);
});

test('fase 19b: webpagina kaarten, vol → op de wachtlijst', async ({ page }) => {
  const posts = await serve(page);
  await page.goto('/kaarten/');
  const vrijdag = page.getByRole('article').filter({ hasText: 'Pronkzitting vrijdag' });
  await expect(vrijdag.getByText('Vol')).toBeVisible();
  await vrijdag.getByRole('button', { name: 'Wachtlijst' }).click();
  await page.getByLabel('Naam').fill('Kees');
  await page.getByLabel('E-mailadres').fill('kees@example.com');
  await page.getByRole('button', { name: 'Op de wachtlijst' }).click();
  await expect(page.getByRole('heading', { name: 'Je staat op de wachtlijst' })).toBeVisible();
  expect(posts[0]).toMatchObject({ path: '/waitlist', body: { productId: 'p-vr', paidQuantity: 1, channel: 'Web' } });
});
