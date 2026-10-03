import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 24: gegevens uit e-Boekhouden bewerken (portal wint) en accountverzoeken met alleen een e-mailadres. */

test('fase 24: gegevens van een lid bewerken en teruggeven aan e-Boekhouden', async ({ page }) => {
  const api = new MockApi(['member.read', 'member.update']);
  await api.install(page);
  await page.goto('/beheer/leden/m-1');

  const card = page.getByRole('region', { name: 'Gegevens van het lid' });
  await expect(card.getByText('piet@example.com')).toBeVisible();
  await card.getByRole('button', { name: 'Bewerken' }).click();
  await card.getByLabel('E-mailadres').fill('piet.nieuw@example.com');
  await card.getByLabel('Plaats').fill('Didam');

  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(
    results.violations
      .filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);

  await card.getByRole('button', { name: 'Gegevens opslaan' }).click();
  await expect(card.getByText(/Gegevens opgeslagen/)).toBeVisible();
  await expect(card.getByText('piet.nieuw@example.com')).toBeVisible();
  await expect(card.getByText('handmatig', { exact: true })).toHaveCount(2);
  expect(api.memberLocalFields['m-1']).toEqual(['email', 'city']);

  await card.getByRole('button', { name: 'Teruggeven aan e-Boekhouden' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Teruggeven' }).click();
  await expect(card.getByText(/neemt de gegevens weer over/)).toBeVisible();
  await expect(card.getByText('handmatig', { exact: true })).toHaveCount(0);
});

test('fase 24: zonder bewerkrecht geen knop Bewerken', async ({ page }) => {
  const api = new MockApi(['member.read']);
  await api.install(page);
  await page.goto('/beheer/leden/m-1');
  await expect(page.getByRole('region', { name: 'Gegevens van het lid' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Bewerken' })).toHaveCount(0);
});

test('fase 24: accountverzoek met alleen een e-mailadres; het bestuur kiest het lid', async ({ page }) => {
  const api = new MockApi(['member.read', 'member.approve']);
  api.accountRequests.push({
    id: 'r-3',
    memberNumber: null,
    email: 'gezin@example.com',
    status: 'Pending',
    mismatchReason: 'multiple-members',
    rejectionReason: null,
    requestedAt: '2026-10-03T08:00:00Z',
    decidedAt: null,
    member: null,
    candidates: [
      { id: 'm-1', memberNumber: '001', fullName: 'Piet van der Berg', email: 'gezin@example.com', status: 'Active' },
      { id: 'm-2', memberNumber: '002', fullName: 'Anna Jansen', email: 'gezin@example.com', status: 'Active' },
    ],
  });
  await api.install(page);
  await page.goto('/beheer/accountverzoeken');

  await expect(page.getByText('Meerdere leden met dit e-mailadres: kies het lid')).toBeVisible();
  const create = page.getByRole('button', { name: 'Account aanmaken voor gezin@example.com' });
  await expect(create).toBeDisabled();
  await page.getByLabel('Kies het lid voor gezin@example.com').selectOption('m-2');
  await page.getByRole('button', { name: 'Account aanmaken voor Anna Jansen' }).click();
  await expect(page.getByText('Het account voor Anna Jansen wordt aangemaakt met gezin@example.com.')).toBeVisible();
  expect(api.accountRequests.find((r) => r.id === 'r-3')?.approvedMemberId).toBe('m-2');
});
