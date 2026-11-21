import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 25: lidmaatschappen per soort en tweepersoonsleden splitsen (hoofdleden aanvinken en mailen). */

test('fase 25: hoofdleden mailen om het tweede lid te registreren', async ({ page }) => {
  const api = new MockApi(['member.read', 'contribution.manage']);
  await api.install(page);
  await page.goto('/beheer/lidmaatschappen');

  await expect(page.getByRole('heading', { name: 'Lidmaatschappen', level: 1 })).toBeVisible();
  await expect(page.getByText('Tweepersoonslid, nog niet gesplitst')).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Nog niet gesplitst (2)' })).toHaveAttribute('aria-selected', 'true');
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

  // Anna heeft geen e-mailadres, dus er valt maar één lid aan te vinken.
  await expect(page.getByRole('checkbox', { name: 'Anna Jansen selecteren' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Alleen wie nog niet gemaild is (1)' }).click();
  await expect(page.getByText('1 geselecteerd')).toBeVisible();
  await page.getByRole('button', { name: 'Mail sturen (1)' }).click();
  await expect(page.getByRole('dialog')).toContainText('verdeeld over de tijd');
  await page.getByRole('dialog').getByRole('button', { name: 'Mails versturen' }).click();

  // Versturen gaat via de outbox: de uitnodiging staat eerst in de wachtrij, de mail volgt.
  await expect(page.getByText('1 mail in de wachtrij gezet.')).toBeVisible();
  await expect(page.getByRole('row', { name: /Piet van der Berg .*In de wachtrij/ })).toBeVisible();
  await expect(page.getByText('0 geselecteerd')).toBeVisible();

  // Wie al gemaild is kun je opnieuw aanvinken; dat wordt een nieuwe link.
  await page.getByRole('checkbox', { name: 'Piet van der Berg selecteren' }).check();
  await page.getByRole('button', { name: 'Mail sturen (1)' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Mails versturen' }).click();
  await expect(page.getByText('1 mail in de wachtrij gezet.')).toBeVisible();
  expect(api.splitCandidates[0]!.timesInvited).toBe(2);
});

test('fase 25: de leden die al gesplitst zijn staan onder een eigen tab', async ({ page }) => {
  const api = new MockApi(['member.read', 'contribution.manage']);
  await api.install(page);
  await page.goto('/beheer/lidmaatschappen');

  // Een gesplitst lid staat niet bij de leden die nog een mail kunnen krijgen.
  await expect(page.getByRole('row', { name: /Henk Bruggink/ })).toHaveCount(0);

  await page.getByRole('tab', { name: 'Gesplitst (1)' }).click();
  await expect(
    page.getByRole('row', { name: /003 Henk Bruggink Truus Bruggink henk@example.com Gesplitst/ }),
  ).toBeVisible();
  // Het tweede lid is een eigen lid, dus je klikt vanaf hier door naar zijn ledenpagina.
  await expect(page.getByRole('link', { name: 'Truus Bruggink' })).toHaveAttribute('href', '/beheer/leden/m-4');
  await expect(page.getByRole('button', { name: /Mail sturen/ })).toHaveCount(0);
});

test('op een smal scherm loopt niets buiten beeld', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const api = new MockApi(['member.read', 'contribution.manage']);
  await api.install(page);
  await page.goto('/beheer/lidmaatschappen');

  await expect(page.getByRole('heading', { name: 'Lidmaatschappen', level: 1 })).toBeVisible();
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(1);
});
