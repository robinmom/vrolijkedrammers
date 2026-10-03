import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 25: lidmaatschappen per soort en tweepersoonsleden splitsen (hoofdlid mailen). */

test('fase 25: hoofdleden mailen om het tweede lid te registreren', async ({ page }) => {
  const api = new MockApi(['member.read', 'contribution.manage']);
  await api.install(page);
  await page.goto('/beheer/lidmaatschappen');

  await expect(page.getByRole('heading', { name: 'Lidmaatschappen', level: 1 })).toBeVisible();
  await expect(page.getByText('Tweepersoonslid, nog niet gesplitst')).toBeVisible();
  await expect(
    page.getByRole('row', { name: /001 Piet van der Berg Marie van der Berg piet@example.com Nog niet gemaild/ }),
  ).toBeVisible();
  await expect(page.getByRole('row', { name: /002 Anna Jansen onbekend geen e-mailadres/ })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Tarieven per jaar' })).toBeVisible();

  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);

  await page.getByRole('button', { name: 'Iedereen mailen die nog niet gemaild is (1)' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Mails versturen' }).click();
  await expect(page.getByText('1 mail verstuurd, 1 zonder e-mailadres overgeslagen.')).toBeVisible();
  await expect(page.getByRole('row', { name: /Piet van der Berg .*Gemaild/ })).toBeVisible();
  await page.getByRole('button', { name: 'Opnieuw mailen: Piet van der Berg' }).click();
  await expect(page.getByText('1 mail verstuurd.')).toBeVisible();
  expect(api.splitCandidates[0]!.timesInvited).toBe(2);
});
