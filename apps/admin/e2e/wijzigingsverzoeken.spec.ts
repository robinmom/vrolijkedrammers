import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 26: wijzigingen en het verbreken van combinaties goedkeuren of afwijzen. */

test('fase 26: wijziging goedkeuren en verbreken afwijzen', async ({ page }) => {
  const api = new MockApi(['member.read', 'member.update']);
  await api.install(page);
  await page.goto('/beheer/wijzigingsverzoeken');

  await expect(page.getByRole('heading', { name: 'Wijzigingsverzoeken', level: 1 })).toBeVisible();
  const change = page.getByRole('article', { name: 'Wijziging van Piet van der Berg' });
  await expect(change.getByRole('row', { name: 'Adres Dorpsstraat 1 Kerkstraat 5' })).toBeVisible();
  await expect(change.getByRole('row', { name: /IBAN — \*\*\*\* 4300/ })).toBeVisible();

  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);

  await change.getByRole('button', { name: 'Wijziging van Piet van der Berg goedkeuren' }).click();
  await expect(page.getByText('De wijziging van Piet van der Berg is doorgevoerd.')).toBeVisible();
  expect(api.audit.map((a) => a.action)).toContain('member.changes-approve');

  const breakRequest = page.getByRole('article', { name: 'Verbreken Piet van der Berg en Anna Jansen' });
  await expect(breakRequest.getByText('Beide akkoord: wacht op goedkeuring')).toBeVisible();
  await expect(breakRequest.getByText('**** 1234 (A. Jansen)')).toBeVisible();
  await breakRequest.getByRole('button', { name: 'Afwijzen' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Reden').fill('Eerst even bellen');
  await dialog.getByRole('button', { name: 'Afwijzen' }).click();
  await expect(page.getByText('Het verzoek is afgewezen.')).toBeVisible();
  await expect(page.getByText('Geen lopende verzoeken.')).toBeVisible();
});
