import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 20: jubilarissen per carnavalsjaar, het jubileumjaar per lid aanpassen en de jubilea instellen. */

test('fase 20: jubilarissen, correctie bij het lid en eigen jubilea', async ({ page }) => {
  const api = new MockApi(['member.read', 'member.update', 'member.export', 'config.manage']);
  await api.install(page);
  await page.goto('/beheer/jubilarissen');

  await expect(page.getByRole('heading', { name: 'Jubilarissen', level: 1 })).toBeVisible();
  await expect(
    page.getByText(
      /valt in 2027\. Jubilaris zijn de actieve leden met inschrijfjaar 2016 \(11 jaar\), 2005 \(22 jaar\)/,
    ),
  ).toBeVisible();
  await expect(page.getByText('Geen jubilarissen in dit carnavalsjaar.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Zonder inschrijfjaar (1)' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Anna Jansen' })).toBeVisible();

  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exporteren (Excel)' }).click();
  expect((await download).suggestedFilename()).toBe('jubilarissen-2026-2027.xlsx');

  // Piet (inschrijfjaar 1995) was een tijd geen lid: zijn jubileum telt vanaf 1994 → 33 jaar in 2027.
  await page.goto('/beheer/leden/m-1');
  const card = page.getByRole('region', { name: 'Jubileum' });
  await expect(card.getByText('Het jubileum telt vanaf 1995.')).toBeVisible();
  await card.getByLabel('Jubileum telt vanaf (jaar)').fill('1994');
  await card.getByLabel('Reden').fill('Eerder lid via de jeugd');
  await card.getByRole('button', { name: 'Jubileum opslaan' }).click();
  await expect(card.getByText('Jubileum opgeslagen.')).toBeVisible();
  expect(api.audit.map((a) => a.action)).toContain('member.jubilee-year.changed');

  await page.goto('/beheer/jubilarissen');
  await expect(page.getByRole('heading', { name: '33 jaar lid (1)' })).toBeVisible();
  await expect(
    page.getByRole('row', { name: /001 Piet van der Berg 1995 telt vanaf 1994 Eerder lid via de jeugd/ }),
  ).toBeVisible();

  await page.getByLabel('Jubilea (aantal jaren lid)').fill('11, 22');
  await page.getByRole('button', { name: 'Opslaan', exact: true }).click();
  await expect(page.getByText('Jubilea opgeslagen.')).toBeVisible();
  await expect(page.getByText('Geen jubilarissen in dit carnavalsjaar.')).toBeVisible();
});

test('fase 20: zonder export- of configuratierecht geen knoppen', async ({ page }) => {
  const api = new MockApi(['member.read']);
  await api.install(page);
  await page.goto('/beheer/jubilarissen');
  await expect(page.getByRole('heading', { name: 'Zonder inschrijfjaar (1)' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Exporteren (Excel)' })).toHaveCount(0);
  await expect(page.getByLabel('Jubilea (aantal jaren lid)')).toHaveCount(0);
});
