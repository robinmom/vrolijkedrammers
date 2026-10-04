import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 27a: mailinggroepen, een mailing opstellen met voorbeeld, testmail en versturen. */

async function expectNoSeriousA11yIssues(page: Page) {
  // Het voorbeeld-iframe is de e-mail zelf (afgeschermd, sandbox), geen onderdeel van het portal: niet meescannen.
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
    .exclude('.mailing-frame')
    .analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);
}

test('fase 27a: mailinggroep met leden, ledengroep en losse adressen', async ({ page }) => {
  const api = new MockApi(['mailing.manage', 'member.read']);
  await api.install(page);
  await page.goto('/beheer/mailing/groepen');

  await expect(page.getByRole('heading', { name: 'Mailinggroepen', level: 1 })).toBeVisible();
  await expect(page.getByRole('row', { name: /Alle leden .*alle leden/ })).toBeVisible();
  await expect(page.getByText('weg@example.com')).toBeVisible();
  await expectNoSeriousA11yIssues(page);

  await page.getByRole('link', { name: 'Groep toevoegen' }).click();
  await page.getByLabel('Naam').fill('Pronkzitting gasten');
  await page.getByLabel('Jeugdcommissie').check();
  await page.getByLabel('Lid zoeken').fill('Piet');
  await page.getByRole('list', { name: 'Zoekresultaten' }).getByRole('button', { name: 'Toevoegen' }).first().click();
  await page.getByLabel(/Losse e-mailadressen/).fill('Gerda Gast <gerda@example.com>\njan@example.com; Jan Jansen');
  await expect(page.getByText('Losse e-mailadressen (2)')).toBeVisible();
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Groep opgeslagen.')).toBeVisible();
  expect(api.mailingLists.at(-1)).toMatchObject({
    name: 'Pronkzitting gasten',
    allMembers: false,
    groupIds: ['g-1'],
    memberIds: ['m-1'],
    addresses: [
      { email: 'gerda@example.com', name: 'Gerda Gast' },
      { email: 'jan@example.com', name: 'Jan Jansen' },
    ],
  });
  await expectNoSeriousA11yIssues(page);
});

test('fase 27a: mailing opstellen met blokken, voorbeeld, testmail en versturen', async ({ page }) => {
  const api = new MockApi(['mailing.manage']);
  await api.install(page);
  await page.goto('/beheer/mailing');
  await expect(page.getByText('Nog geen mailings.')).toBeVisible();
  await page.getByRole('link', { name: 'Nieuwe mailing' }).click();

  await page.getByLabel('Soort').selectOption({ label: 'Uitnodiging' });
  await page.getByLabel('Onderwerp').fill('Uitnodiging pronkzitting 2027');
  await page.getByLabel('Alle leden').check();
  await page.getByLabel('Tekst', { exact: true }).fill('Beste {voornaam},\n\nKom je ook?');
  await page.getByRole('button', { name: '+ Knop' }).click();
  await page.getByLabel('Knoptekst').fill('Bestel kaarten');
  await page.getByLabel('Link', { exact: true }).fill('https://www.vrolijkedrammers.nl/kaarten/');

  // Het voorbeeld zoals Piet hem krijgt, met het aantal ontvangers.
  await expect(page.getByText('120 ontvangers, 1 afgemeld')).toBeVisible();
  await expect(page.frameLocator('iframe[title="Voorbeeld van de mailing"]').getByText('Beste Piet,')).toBeVisible();

  // Blok omhoog: de knop komt boven de tekst.
  await page.getByRole('button', { name: 'Blok 3 omhoog' }).click();
  await page.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('Mailing opgeslagen.')).toBeVisible();
  expect(api.mailings[0]).toMatchObject({ kind: 'Invitation', listIds: ['ml-1'] });
  expect(api.mailings[0]!.blocks.map((b) => b.type)).toEqual(['heading', 'button', 'text']);
  await expectNoSeriousA11yIssues(page);

  await page.getByRole('button', { name: 'Testmail naar mij' }).click();
  await expect(page.getByText('Testmail verstuurd naar bestuur@example.com.')).toBeVisible();
  expect(api.mailingTests).toBe(1);

  await page.getByRole('button', { name: 'Versturen…' }).click();
  await expect(page.getByRole('dialog')).toContainText('De mailing gaat naar 120 ontvangers');
  await page.getByRole('dialog').getByRole('button', { name: 'Versturen' }).click();
  await expect(page.getByRole('heading', { name: 'Versturen' })).toBeVisible();
  await expect(page.getByText('0 verstuurd, 120 in de wachtrij')).toBeVisible();
  await expect(page.getByLabel('Onderwerp')).toBeDisabled();

  // Een verstuurde mailing kopiëren voor volgend jaar.
  await page.getByRole('button', { name: 'Kopie maken' }).click();
  await expect(page).toHaveURL(/\/mailing\/mail-2$/);
  await expect(page.getByLabel('Onderwerp')).toBeEnabled();
});
