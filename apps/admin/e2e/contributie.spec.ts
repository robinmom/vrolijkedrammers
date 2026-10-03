import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 23a: contributie per lid, tarieven en het lidmaatschap van een lid (partner en vrijstelling). */

test('fase 23a: contributie, tarief toevoegen en een partner koppelen', async ({ page }) => {
  const api = new MockApi(['member.read', 'member.update', 'contribution.manage']);
  await api.install(page);
  await page.goto('/beheer/contributie');

  await expect(page.getByRole('heading', { name: 'Contributie', level: 1 })).toBeVisible();
  await expect(
    page.getByRole('row', { name: /001 Piet van der Berg Twee personen \(e-Boekhouden\) € 57,50 Betaalt/ }),
  ).toBeVisible();
  await expect(
    page.getByRole('row', { name: /002 Anna Jansen Onbekend € 0,00 Onbekend Soort lidmaatschap onbekend/ }),
  ).toBeVisible();
  await page.getByLabel('Filter').selectOption('attention');
  await expect(page.getByRole('row', { name: /Piet van der Berg/ })).toHaveCount(0);

  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);

  // Nieuw tarief per 2027.
  const rates = page.getByRole('region', { name: 'Tarieven per jaar' });
  await rates.getByLabel('Geldig vanaf').fill('2027-01-01');
  await rates.getByLabel('Eén persoon (€)').fill('35');
  await rates.getByLabel('Twee personen (€)').fill('60');
  await rates.getByLabel('Eén persoon 65+ (€)').fill('24');
  await rates.getByLabel('Twee personen 65+ (€)').fill('46');
  await rates.getByLabel('Dansgarde (€)').fill('90,00');
  await rates.getByRole('button', { name: 'Tarief opslaan' }).click();
  await expect(rates.getByText('Tarief opgeslagen.')).toBeVisible();
  expect(api.contributionRates[0]).toMatchObject({ validFrom: '2027-01-01', onePerson: 35, dansgarde: 90 });

  // Anna is de partner van Piet.
  await page.goto('/beheer/leden/m-2');
  const card = page.getByRole('region', { name: 'Lidmaatschap en contributie' });
  await card.getByLabel('Soort lidmaatschap').selectOption('Partner');
  await card.getByLabel('Zoek het lid dat betaalt').fill('Piet');
  await card.getByRole('button', { name: 'Piet van der Berg kiezen als betaler' }).click();
  await expect(card.getByRole('link', { name: 'Piet van der Berg (001)' })).toBeVisible();
  await card.getByRole('button', { name: 'Lidmaatschap opslaan' }).click();
  await expect(card.getByText('Lidmaatschap opgeslagen.')).toBeVisible();
  expect(api.membershipSettings['m-2']).toMatchObject({ kind: 'Partner', payerMemberId: 'm-1', exempt: false });

  // Piet is Convent: vrijgesteld.
  await page.goto('/beheer/leden/m-1');
  const piet = page.getByRole('region', { name: 'Lidmaatschap en contributie' });
  await piet.getByLabel('Soort lidmaatschap').selectOption('TwoPersons');
  await piet.getByLabel('Vrijgesteld van contributie').check();
  await piet.getByLabel('Reden').fill('Convent');
  await piet.getByRole('button', { name: 'Lidmaatschap opslaan' }).click();
  await expect(piet.getByText('Lidmaatschap opgeslagen.')).toBeVisible();
  expect(api.membershipSettings['m-1']).toMatchObject({ kind: 'TwoPersons', exempt: true, exemptReason: 'Convent' });
});

test('fase 23a: zonder contributierecht geen menu en geen kaart bij het lid', async ({ page }) => {
  const api = new MockApi(['member.read', 'member.update']);
  await api.install(page);
  await page.goto('/beheer/leden/m-1');
  await expect(page.getByRole('heading', { name: 'Gegevens van de app' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Lidmaatschap en contributie' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Contributie' })).toHaveCount(0);
});
