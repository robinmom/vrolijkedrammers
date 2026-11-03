import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 27s: aanmeldingen uit de app beoordelen, en adverteerders verwijderen met een prullenbak. */

async function axeClean(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);
}

test('nieuwe adverteerders: meer info vragen, goedkeuren en afwijzen', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  await api.install(page);

  // Een aanmelding uit de app telt nog niet mee: hij staat niet in het overzicht.
  await page.goto('/beheer/adverteerders');
  await expect(page.getByRole('link', { name: 'Bakkerij De Test' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Kapsalon Nieuw' })).toBeHidden();

  await page.goto('/beheer/adverteerders/aanmeldingen');
  await expect(page.getByRole('heading', { name: 'Nieuwe adverteerders', level: 1 })).toBeVisible();
  const aanmelding = page.getByRole('region', { name: 'Kapsalon Nieuw' });
  await expect(aanmelding.getByText('Aangemeld door Piet van der Berg')).toBeVisible();
  await expect(aanmelding.getByText('Wil graag een halve pagina')).toBeVisible();
  await axeClean(page);

  // Meer info nodig: de vraag blijft bij de aanmelding staan.
  await aanmelding.getByRole('button', { name: 'Meer info nodig' }).click();
  await page.getByLabel('Vraag aan de collectant').fill('Wat is het e-mailadres?');
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Vraag bij Kapsalon Nieuw gezet.')).toBeVisible();
  await expect(aanmelding.getByText('Jouw vraag: Wat is het e-mailadres?')).toBeVisible();
  await expect(aanmelding.getByText('meer info nodig', { exact: true })).toBeVisible();

  // Goedkeuren: uit de wachtrij en vanaf nu in het overzicht.
  await aanmelding.getByRole('button', { name: 'Goedkeuren' }).click();
  await expect(page.getByText('Kapsalon Nieuw is goedgekeurd en doet mee in de campagne.')).toBeVisible();
  await expect(page.getByText('Geen nieuwe aanmeldingen.')).toBeVisible();
  expect(api.audit.map((a) => a.action)).toContain('advertiser.approved');

  await page.goto('/beheer/adverteerders');
  await expect(page.getByRole('link', { name: 'Kapsalon Nieuw' })).toBeVisible();
});

test('nieuwe adverteerders: afwijzen met reden gaat naar de verwijderde adverteerders', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  await api.install(page);
  await page.goto('/beheer/adverteerders/aanmeldingen');

  await page.getByRole('region', { name: 'Kapsalon Nieuw' }).getByRole('button', { name: 'Afwijzen' }).click();
  await page.getByLabel('Reden').fill('Dubbel aangemeld');
  await page.getByRole('button', { name: 'Afwijzen' }).last().click();
  await expect(page.getByText('Kapsalon Nieuw is afgewezen.')).toBeVisible();
  await expect(page.getByText('Geen nieuwe aanmeldingen.')).toBeVisible();
  expect(api.audit.map((a) => a.action)).toContain('advertiser.rejected');

  await page.goto('/beheer/adverteerders/verwijderd');
  await expect(page.getByRole('row', { name: /Kapsalon Nieuw.*Dubbel aangemeld/ })).toBeVisible();
});

test('adverteerder verwijderen, terugzetten en definitief wissen', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  // Opgehaald, zodat er verderop een factuur gemaakt kan worden; de aanmelding uit de app blijft erbuiten.
  api.advertisers.forEach((a) => (a.status2027 = 'Collected'));
  await api.install(page);

  // Verwijderen vanaf de detailpagina; daarna staat hij in de prullenbak.
  await page.goto('/beheer/adverteerders/adv-2');
  const kaart = page.getByRole('region', { name: 'Verwijderen' });
  await kaart.getByLabel('Reden (niet verplicht)').fill('Gestopt met adverteren');
  await kaart.getByRole('button', { name: 'Adverteerder verwijderen' }).click();
  await page.getByRole('button', { name: 'Verwijderen', exact: true }).click();

  await expect(page.getByRole('heading', { name: 'Verwijderde adverteerders', level: 1 })).toBeVisible();
  await expect(page.getByRole('row', { name: /Garage Proef.*Gestopt met adverteren/ })).toBeVisible();
  await expect(page.getByText('1 verwijderd')).toBeVisible();
  await axeClean(page);
  await page.goto('/beheer/adverteerders');
  await expect(page.getByRole('link', { name: 'Garage Proef' })).toBeHidden();

  // Zonder facturen mag hij definitief weg.
  await page.goto('/beheer/adverteerders/verwijderd');
  await page
    .getByRole('row', { name: /Garage Proef/ })
    .getByRole('button', { name: 'Definitief verwijderen' })
    .click();
  await page.getByRole('button', { name: 'Definitief verwijderen' }).last().click();
  await expect(page.getByText('Garage Proef is definitief verwijderd.')).toBeVisible();
  await expect(page.getByText('Er is niets verwijderd.')).toBeVisible();
  expect(api.audit.map((a) => a.action)).toContain('advertiser.purged');

  // Met een factuur blijft hij bewaard: geen knop om definitief te wissen.
  await page.goto('/beheer/adverteerders/facturen');
  await page.getByRole('button', { name: /Facturen maken/ }).click();
  await expect(page.getByText(/facturen gemaakt/)).toBeVisible();

  await page.goto('/beheer/adverteerders/adv-1');
  await page
    .getByRole('region', { name: 'Verwijderen' })
    .getByRole('button', { name: 'Adverteerder verwijderen' })
    .click();
  await page.getByRole('button', { name: 'Verwijderen', exact: true }).click();
  const rij = page.getByRole('row', { name: /Bakkerij De Test/ });
  await expect(rij.getByText('Blijft bewaard: 1 factuur')).toBeVisible();
  await expect(rij.getByRole('button', { name: 'Definitief verwijderen' })).toBeHidden();

  // Terugzetten brengt hem weer in het overzicht.
  await rij.getByRole('button', { name: 'Terugzetten' }).click();
  await expect(page.getByText('Bakkerij De Test staat weer in de lijst.')).toBeVisible();
  await page.goto('/beheer/adverteerders');
  await expect(page.getByRole('link', { name: 'Bakkerij De Test' })).toBeVisible();
});

test('overzicht: filteren op wie meedoet', async ({ page }) => {
  const api = new MockApi(['advertiser.manage']);
  await api.install(page);
  await page.goto('/beheer/adverteerders');

  await page.getByLabel('Meedoen').selectOption('nee');
  await expect(page.getByRole('link', { name: 'Bakkerij De Test' })).toBeHidden();
  await page.getByLabel('Meedoen').selectOption('ja');
  await expect(page.getByRole('link', { name: 'Bakkerij De Test' })).toBeVisible();
});

test('nieuwe en verwijderde adverteerders: geen horizontale overflow op een telefoon', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const api = new MockApi(['advertiser.manage']);
  await api.install(page);

  for (const path of ['/beheer/adverteerders/aanmeldingen', '/beheer/adverteerders/verwijderd']) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  }
});
