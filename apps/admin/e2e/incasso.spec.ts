import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 23c: SEPA-incasso: gegevens van de vereniging, voorbeeld, run en bestand. */

test('fase 23c: incassorun maken en het bestand downloaden', async ({ page }) => {
  const api = new MockApi(['member.read', 'contribution.manage']);
  await api.install(page);
  await page.goto('/beheer/incasso');

  await page.getByLabel('Incassodatum').fill('2027-03-01');
  await expect(
    page.getByText('Vul eerst de gegevens van de vereniging in (naam, IBAN en incassant-ID).'),
  ).toBeVisible();
  await expect(page.getByRole('button', { name: 'Incassorun maken (1)' })).toBeDisabled();

  const creditor = page.getByRole('region', { name: 'Vereniging als incassant' });
  await creditor.getByLabel('Naam').fill('CV De Vrolijke Drammers');
  await creditor.getByLabel('IBAN van de vereniging').fill('NL39RABO0300065264');
  await creditor.getByLabel('Incassant-ID').fill('NL12ZZZ123456780000');
  await creditor.getByRole('button', { name: 'Opslaan' }).click();
  await expect(creditor.getByText('Gegevens van de vereniging opgeslagen.')).toBeVisible();

  await expect(
    page.getByRole('row', { name: /001 Piet van der Berg Combinatie € 57,50 \*\*\*\* 4300 M-001/ }),
  ).toBeVisible();
  await expect(page.getByRole('row', { name: /002 Anna Jansen € 32,50 Geen IBAN of machtiging/ })).toBeVisible();

  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);

  await page.getByRole('button', { name: 'Incassorun maken (1)' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Run maken' }).click();
  await expect(page.getByText('De incassorun is gemaakt. Download het bestand hieronder.')).toBeVisible();

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: /Bestand downloaden: incasso/ }).click();
  expect((await download).suggestedFilename()).toBe('incasso-20270301.xml');
  await expect(page.getByRole('row', { name: /Contributie 2027 .* 1 € 57,50 3 okt 2026/ })).toBeVisible();
});
